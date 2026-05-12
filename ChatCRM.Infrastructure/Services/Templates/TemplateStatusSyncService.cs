using System.Text.Json;
using ChatCRM.Application.Interfaces;
using ChatCRM.Application.Templates.Exceptions;
using ChatCRM.Domain.Entities;
using ChatCRM.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ChatCRM.Infrastructure.Services.Templates
{
    /// <summary>
    /// Background poller that keeps locally-Submitted templates' status in sync with Meta.
    ///
    /// Cadence (adaptive — most templates resolve in &lt;1 hour, so we poll fast initially
    /// and back off as the row gets older):
    ///   ≤30 min from SubmittedAt   → every  2 min
    ///   30 min – 6 h               → every 10 min
    ///   6 h – 24 h                 → every 30 min
    ///   24 h – 7 d                 → every  2 h
    ///   &gt;7 d                       → mark Stuck and stop polling (manual sync still works)
    ///
    /// Outer tick runs once per minute and decides per-row whether the row's individual
    /// cadence is due. Skips entirely when the provider isn't configured.
    /// </summary>
    public sealed class TemplateStatusSyncService : BackgroundService
    {
        private static readonly TimeSpan OuterTickInterval = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan StuckThreshold = TimeSpan.FromDays(7);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<TemplateStatusSyncService> _logger;

        public TemplateStatusSyncService(IServiceScopeFactory scopeFactory, ILogger<TemplateStatusSyncService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Stagger startup so a fresh app boot doesn't slam Meta the moment migrations run.
            try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); }
            catch (OperationCanceledException) { return; }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await TickAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // Never let the background loop die — log and try again next tick.
                    _logger.LogError(ex, "[TEMPLATES-SYNC] Tick failed; will retry next interval.");
                }

                try { await Task.Delay(OuterTickInterval, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }

        private async Task TickAsync(CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();
            var sp = scope.ServiceProvider;
            var provider = sp.GetRequiredService<IWhatsAppTemplateProvider>();

            // No WABA wired up — nothing to poll.
            if (!provider.IsConfigured) return;

            var db = sp.GetRequiredService<AppDbContext>();
            var health = sp.GetRequiredService<ITemplateProviderHealth>();

            var nowUtc = DateTime.UtcNow;

            // Use the (Status, SubmittedAt) composite index — only Submitted rows with a Meta id.
            var rows = await db.WhatsAppTemplates
                .Where(t => t.Status == WhatsAppTemplateStatus.Submitted
                         && t.MetaTemplateId != null
                         && t.SubmittedAt != null)
                .OrderBy(t => t.StatusCheckedAt) // oldest checked first → most overdue first
                .ToListAsync(ct);

            if (rows.Count == 0) return;

            var anyChanges = false;

            foreach (var row in rows)
            {
                if (ct.IsCancellationRequested) break;

                // Stuck cap: 7 days in Submitted with no resolution → give up auto-polling.
                // Manual "Sync from Meta" can still rescue it.
                var ageInSubmitted = nowUtc - (row.SubmittedAt ?? nowUtc);
                if (ageInSubmitted > StuckThreshold)
                {
                    row.Status = WhatsAppTemplateStatus.Stuck;
                    row.UpdatedAt = nowUtc;
                    db.BillingAuditLogs.Add(new BillingAuditLog
                    {
                        Actor = "system:templates-sync",
                        Action = "templates.stuck",
                        EntityType = nameof(WhatsAppTemplate),
                        EntityId = row.Id.ToString(),
                        AfterJson = JsonSerializer.Serialize(new { row.Name, row.LanguageCode, AgeDays = (int)ageInSubmitted.TotalDays }),
                        AtUtc = nowUtc
                    });
                    anyChanges = true;
                    _logger.LogWarning("[TEMPLATES-SYNC] {Id} '{Name}' stuck after {Days}d — stopping auto-poll.", row.Id, row.Name, (int)ageInSubmitted.TotalDays);
                    continue;
                }

                if (!IsDueForPoll(row, nowUtc, ageInSubmitted)) continue;

                try
                {
                    var status = await provider.GetStatusAsync(row.MetaTemplateId!, ct);
                    if (status is null)
                    {
                        // Meta no longer recognises the id — Business Manager probably deleted it.
                        if (row.Status != WhatsAppTemplateStatus.Disabled)
                        {
                            row.Status = WhatsAppTemplateStatus.Disabled;
                            row.RejectionReason = "Removed in Meta Business Manager.";
                            row.UpdatedAt = nowUtc;
                            anyChanges = true;
                        }
                    }
                    else if (status.MappedStatus != row.Status)
                    {
                        _logger.LogInformation(
                            "[TEMPLATES-SYNC] {Id} '{Name}' {From} → {To} ({Raw})",
                            row.Id, row.Name, row.Status, status.MappedStatus, status.RawStatus);
                        row.Status = status.MappedStatus;
                        row.RejectionReason = status.Reason;
                        row.UpdatedAt = nowUtc;
                        anyChanges = true;
                    }
                    row.StatusCheckedAt = nowUtc;
                    anyChanges = true;
                    health.Reset();
                }
                catch (TemplateAuthenticationException ex)
                {
                    health.RecordAuthFailure(ex.Message);
                    _logger.LogWarning(
                        "[TEMPLATES-SYNC] Auth failed (code {Code}); pausing this tick. Rotate the WABA token.",
                        ex.ErrorCode);
                    // Stop the whole tick — every other row would fail with the same error.
                    break;
                }
                catch (HttpRequestException ex)
                {
                    // Transient — leave row untouched, the next tick will retry.
                    _logger.LogWarning(ex, "[TEMPLATES-SYNC] Transient error polling {Id}; retrying next tick.", row.Id);
                }
            }

            if (anyChanges)
            {
                try
                {
                    await db.SaveChangesAsync(ct);
                }
                catch (DbUpdateException ex)
                {
                    _logger.LogError(ex, "[TEMPLATES-SYNC] Save failed; rolling back this tick.");
                }
            }
        }

        /// <summary>
        /// Adaptive cadence — drives both polling frequency and back-off as a row ages without
        /// resolution. <paramref name="ageInSubmitted"/> is time since SubmittedAt.
        /// </summary>
        private static bool IsDueForPoll(WhatsAppTemplate row, DateTime nowUtc, TimeSpan ageInSubmitted)
        {
            var cadence =
                ageInSubmitted < TimeSpan.FromMinutes(30) ? TimeSpan.FromMinutes(2) :
                ageInSubmitted < TimeSpan.FromHours(6)    ? TimeSpan.FromMinutes(10) :
                ageInSubmitted < TimeSpan.FromHours(24)   ? TimeSpan.FromMinutes(30) :
                                                            TimeSpan.FromHours(2);

            // Never polled before → due now (within a small jitter to spread load on app start).
            if (row.StatusCheckedAt is null) return true;

            return (nowUtc - row.StatusCheckedAt.Value) >= cadence;
        }
    }
}
