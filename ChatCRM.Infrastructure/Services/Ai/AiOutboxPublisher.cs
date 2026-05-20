using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ChatCRM.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace ChatCRM.Infrastructure.Services.Ai
{
    /// <summary>
    /// Drains <c>AiOutboxMessages</c> rows where <c>Status='pending'</c> and publishes them
    /// to the Redis Stream named by <see cref="AiOptions.InboundStream"/>. On success the
    /// row is marked <c>sent</c> with the Redis-assigned stream ID. Failures bump
    /// <c>Attempts</c> + record <c>LastError</c>; the next tick retries.
    ///
    /// Crash-safety: rows always exist in SQL Server before the XADD attempts; on process
    /// restart, anything still <c>pending</c> is re-attempted. Combined with idempotency on
    /// the AI service side, duplicate publishes are harmless.
    /// </summary>
    public sealed class AiOutboxPublisher : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConnectionMultiplexer _redis;
        private readonly AiOptions _options;
        private readonly ILogger<AiOutboxPublisher> _logger;

        public AiOutboxPublisher(
            IServiceScopeFactory scopeFactory,
            IConnectionMultiplexer redis,
            IOptions<AiOptions> options,
            ILogger<AiOutboxPublisher> logger)
        {
            _scopeFactory = scopeFactory;
            _redis = redis;
            _options = options.Value;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation(
                "[AI-OUTBOX] publisher started — stream={Stream} batch={Batch} poll={Poll}ms",
                _options.InboundStream, _options.PublisherBatchSize, _options.PublisherPollMilliseconds);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var processed = await TickAsync(stoppingToken);
                    if (processed == 0)
                        await Task.Delay(_options.PublisherPollMilliseconds, stoppingToken);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[AI-OUTBOX] publisher tick failed; backing off");
                    try { await Task.Delay(5000, stoppingToken); } catch (OperationCanceledException) { }
                }
            }

            _logger.LogInformation("[AI-OUTBOX] publisher stopped");
        }

        private async Task<int> TickAsync(CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var rows = await db.AiOutboxMessages
                .Where(r => r.Status == "pending")
                .OrderBy(r => r.CreatedAt)
                .Take(_options.PublisherBatchSize)
                .ToListAsync(cancellationToken);

            if (rows.Count == 0) return 0;

            var stream = _redis.GetDatabase();

            foreach (var row in rows)
            {
                try
                {
                    Dictionary<string, string>? payload;
                    try
                    {
                        payload = JsonSerializer.Deserialize<Dictionary<string, string>>(row.PayloadJson);
                    }
                    catch (JsonException ex)
                    {
                        row.Status = "error";
                        row.LastError = $"corrupt_payload: {ex.Message}";
                        row.Attempts++;
                        continue;
                    }

                    if (payload is null || payload.Count == 0)
                    {
                        row.Status = "error";
                        row.LastError = "empty_payload";
                        row.Attempts++;
                        continue;
                    }

                    var entries = payload
                        .Select(kv => new NameValueEntry(kv.Key, kv.Value ?? string.Empty))
                        .ToArray();

                    var streamId = await stream.StreamAddAsync(_options.InboundStream, entries);
                    row.Status = "sent";
                    row.StreamId = streamId.ToString();
                    row.SentAt = DateTime.UtcNow;
                    row.Attempts++;
                }
                catch (Exception ex)
                {
                    row.Attempts++;
                    row.LastError = ex.Message?.Length > 500 ? ex.Message[..500] : ex.Message;
                    _logger.LogWarning(ex,
                        "[AI-OUTBOX] XADD failed for row {RowId} (attempt {Attempts})",
                        row.Id, row.Attempts);
                    // Stay in 'pending' so next tick retries.
                }
            }

            await db.SaveChangesAsync(cancellationToken);
            return rows.Count;
        }
    }
}
