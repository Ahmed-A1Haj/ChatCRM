using ChatCRM.Application.Interfaces;
using ChatCRM.Domain.Entities;
using ChatCRM.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ChatCRM.Infrastructure.Services.Transcription
{
    /// <summary>
    /// Background worker that turns queued audio messages into text. It polls the DB for
    /// <see cref="MessageKind.Audio"/> messages in <see cref="TranscriptionStatus.Pending"/>,
    /// calls the configured <see cref="ITranscriptionService"/> off the request thread, and
    /// writes the result back onto the message — with retry + exponential backoff on transient
    /// failures and a permanent <see cref="TranscriptionStatus.Failed"/> after max attempts.
    /// Completion is pushed to open chats over SignalR.
    /// </summary>
    public sealed class TranscriptionWorker : BackgroundService
    {
        private const int BatchSize = 5;

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IWebHostEnvironment _env;
        private readonly IHubContext<ChatHub> _hub;
        private readonly TranscriptionOptions _options;
        private readonly ILogger<TranscriptionWorker> _logger;

        public TranscriptionWorker(
            IServiceScopeFactory scopeFactory,
            IWebHostEnvironment env,
            IHubContext<ChatHub> hub,
            IOptions<TranscriptionOptions> options,
            ILogger<TranscriptionWorker> logger)
        {
            _scopeFactory = scopeFactory;
            _env = env;
            _hub = hub;
            _options = options.Value;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); }
            catch (OperationCanceledException) { return; }

            await RecoverStaleProcessingAsync(stoppingToken);

            var interval = TimeSpan.FromSeconds(Math.Max(2, _options.PollSeconds));
            while (!stoppingToken.IsCancellationRequested)
            {
                try { await TickAsync(stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception ex) { _logger.LogError(ex, "[TRANSCRIBE] Tick failed; retrying next interval."); }

                try { await Task.Delay(interval, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }

        /// <summary>A crash mid-call can leave a row stuck in Processing — requeue those on startup.</summary>
        private async Task RecoverStaleProcessingAsync(CancellationToken ct)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await db.Messages
                    .Where(m => m.TranscriptionStatus == TranscriptionStatus.Processing)
                    .ExecuteUpdateAsync(s => s.SetProperty(m => m.TranscriptionStatus, TranscriptionStatus.Pending), ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[TRANSCRIBE] Could not reset stale Processing rows.");
            }
        }

        private async Task TickAsync(CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var transcriber = scope.ServiceProvider.GetRequiredService<ITranscriptionService>();

            var now = DateTime.UtcNow;
            var due = await db.Messages
                .Include(m => m.Conversation)
                .Where(m => m.Kind == MessageKind.Audio
                            && m.MediaUrl != null
                            && m.TranscriptionStatus == TranscriptionStatus.Pending
                            && m.TranscriptionAttemptCount < _options.MaxAttempts
                            && (m.TranscriptionNextAttemptUtc == null || m.TranscriptionNextAttemptUtc <= now))
                .OrderBy(m => m.SentAt)
                .Take(BatchSize)
                .ToListAsync(ct);

            foreach (var message in due)
            {
                if (ct.IsCancellationRequested) break;
                await ProcessAsync(db, transcriber, message, ct);
            }
        }

        private async Task ProcessAsync(AppDbContext db, ITranscriptionService transcriber, Message message, CancellationToken ct)
        {
            var path = ResolveMediaPath(message.MediaUrl);
            if (path is null || !File.Exists(path))
            {
                // The bytes aren't on disk — nothing we can retry. Mark failed.
                await FailAsync(db, message, "Audio file not found on disk.", permanent: true, ct);
                return;
            }

            message.TranscriptionStatus = TranscriptionStatus.Processing;
            message.TranscriptionAttemptCount += 1;
            await db.SaveChangesAsync(ct);

            try
            {
                await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                var result = await transcriber.TranscribeAsync(
                    stream, Path.GetFileName(path), message.MediaMimeType, language: null, ct);

                message.TranscriptionStatus = TranscriptionStatus.Done;
                message.TranscriptionText = result.Text;
                message.TranscriptionLanguage = result.Language;
                message.TranscriptionProvider = string.IsNullOrWhiteSpace(result.Provider) ? transcriber.ProviderName : result.Provider;
                message.TranscriptionError = null;
                message.TranscriptionNextAttemptUtc = null;
                await db.SaveChangesAsync(ct);

                _logger.LogInformation("[TRANSCRIBE] Message {Id} transcribed via {Provider} ({Lang}).",
                    message.Id, message.TranscriptionProvider, message.TranscriptionLanguage ?? "?");

                await BroadcastAsync(message, ct);
            }
            catch (Exception ex)
            {
                var permanent = message.TranscriptionAttemptCount >= _options.MaxAttempts;
                _logger.LogWarning(ex, "[TRANSCRIBE] Message {Id} attempt {Attempt}/{Max} failed{Final}.",
                    message.Id, message.TranscriptionAttemptCount, _options.MaxAttempts, permanent ? " (permanent)" : "");
                await FailAsync(db, message, ex.Message, permanent, ct);
            }
        }

        private async Task FailAsync(AppDbContext db, Message message, string error, bool permanent, CancellationToken ct)
        {
            message.TranscriptionError = error.Length > 1000 ? error[..1000] : error;
            if (permanent)
            {
                message.TranscriptionStatus = TranscriptionStatus.Failed;
                message.TranscriptionNextAttemptUtc = null;
            }
            else
            {
                // Exponential backoff: 20s, 40s, 80s, … capped at 5 minutes.
                var delaySeconds = Math.Min(300, 20 * (int)Math.Pow(2, Math.Max(0, message.TranscriptionAttemptCount - 1)));
                message.TranscriptionStatus = TranscriptionStatus.Pending;
                message.TranscriptionNextAttemptUtc = DateTime.UtcNow.AddSeconds(delaySeconds);
            }
            await db.SaveChangesAsync(ct);

            if (permanent) await BroadcastAsync(message, ct);
        }

        private async Task BroadcastAsync(Message message, CancellationToken ct)
        {
            if (message.Conversation is null) return;
            await _hub.Clients.Group(ChatHub.InstanceGroupName(message.Conversation.WhatsAppInstanceId))
                .SendAsync("MessageTranscribed", new
                {
                    conversationId = message.ConversationId,
                    messageId = message.Id,
                    status = (byte)message.TranscriptionStatus,
                    text = message.TranscriptionText,
                    language = message.TranscriptionLanguage,
                    provider = message.TranscriptionProvider,
                    error = message.TranscriptionError
                }, ct);
        }

        private string? ResolveMediaPath(string? mediaUrl)
        {
            if (string.IsNullOrEmpty(mediaUrl) || !mediaUrl.StartsWith("/media/", StringComparison.OrdinalIgnoreCase))
                return null;
            var root = string.IsNullOrWhiteSpace(_env.WebRootPath)
                ? Path.Combine(_env.ContentRootPath, "wwwroot")
                : _env.WebRootPath;
            return Path.Combine(root, "media", Path.GetFileName(mediaUrl));
        }
    }
}
