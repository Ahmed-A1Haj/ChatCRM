using System.Text.Json;
using ChatCRM.Application.Billing.Exceptions;
using ChatCRM.Application.Interfaces;
using ChatCRM.Domain.Entities;
using ChatCRM.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ChatCRM.Infrastructure.Services.Billing
{
    /// <summary>
    /// Phase-4 send pipeline: quote → window-check → reserve → (caller hits Meta) → commit/release.
    ///
    /// Personal/Baileys instances bypass entirely: those are scrape accounts that don't bill us,
    /// so charging the customer would be inventing revenue against nothing.
    ///
    /// Reservations are recorded as Pending WalletTransactions; the wallet balance only moves at
    /// Commit time. Concurrent send affordability is enforced via WalletService.GetEffectiveBalance,
    /// not the raw wallet — two simultaneous sends can't both clear the check on the same dollars.
    /// </summary>
    public sealed class BillingGate : IBillingGate
    {
        private const decimal NegativeFloorUsd = -1.0m;
        private const int MaxConcurrencyRetries = 3;
        private static readonly TimeSpan ServiceWindow = TimeSpan.FromHours(24);

        private readonly AppDbContext _db;
        private readonly IPricingService _pricing;
        private readonly IWalletService _wallet;
        private readonly ILogger<BillingGate> _logger;

        public BillingGate(
            AppDbContext db,
            IPricingService pricing,
            IWalletService wallet,
            ILogger<BillingGate> logger)
        {
            _db = db;
            _pricing = pricing;
            _wallet = wallet;
            _logger = logger;
        }

        public async Task<BillingReservation?> ReserveAsync(
            Conversation conversation, string actingUserId, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(conversation);

            // Personal (Baileys) instances aren't Cloud-API-backed — Meta doesn't charge us, so
            // we don't charge the customer. Surface as null reservation so caller knows to skip.
            if (conversation.Instance.Integration != WhatsAppIntegration.Business)
                return null;

            var nowUtc = DateTime.UtcNow;
            var inWindow = conversation.LastIncomingAt is { } last && (nowUtc - last) < ServiceWindow;

            // Free-form (agent-typed) sends are always Service category. Templates take the
            // template-aware path below.
            var category = MetaMessageCategory.Service;

            // Service category outside the 24h window is forbidden by Meta — tell the agent.
            if (category == MetaMessageCategory.Service && !inWindow)
                throw new ServiceWindowExpiredException(conversation.LastIncomingAt);

            return await ReserveCoreAsync(conversation, category, inWindow, actingUserId, nowUtc, ct);
        }

        public async Task<BillingReservation?> ReserveForTemplateAsync(
            Conversation conversation, MetaMessageCategory category, string actingUserId, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(conversation);

            // Same Personal bypass as the free-form path — Baileys can't send Cloud-API templates
            // anyway, but a defensive null avoids a wallet movement on an Evolution failure.
            if (conversation.Instance.Integration != WhatsAppIntegration.Business)
                return null;

            // Templates are explicitly Marketing / Utility / Authentication. Service is the
            // free-form runtime classification, not a Meta template category.
            if (category == MetaMessageCategory.Service)
                throw new ArgumentException("Templates cannot use the Service category.", nameof(category));

            var nowUtc = DateTime.UtcNow;
            var inWindow = conversation.LastIncomingAt is { } last && (nowUtc - last) < ServiceWindow;

            // No window check — templates are the answer to a closed window. inWindow is still
            // recorded on the audit row so admin reports can see how many in-window templates
            // were sent (a small inefficiency the agent could have replied free-form to).
            return await ReserveCoreAsync(conversation, category, inWindow, actingUserId, nowUtc, ct);
        }

        private async Task<BillingReservation?> ReserveCoreAsync(
            Conversation conversation, MetaMessageCategory category, bool inWindow,
            string actingUserId, DateTime nowUtc, CancellationToken ct)
        {
            var country = PhoneCountryResolver.Resolve(conversation.Contact.PhoneNumber);

            var quote = await _pricing.GetQuoteAsync(
                category,
                country,
                inServiceWindow: inWindow,
                freeEntryPoint: false,
                atUtc: nowUtc,
                cancellationToken: ct);

            // Free sends don't reserve anything — we still write a $0 MessageBillingRecord at commit.
            if (quote.IsFree)
                return new BillingReservation(null, quote, conversation.Id, actingUserId);

            // Affordability uses *effective* balance (raw balance minus already-Pending holds) so
            // racing sends can't both squeeze through on the same headroom.
            var effective = await _wallet.GetEffectiveBalanceAsync(ct);
            if (effective - quote.ChargedUsd < NegativeFloorUsd)
                throw new InsufficientBalanceException(effective, quote.ChargedUsd);

            var wallet = await _db.Wallets.AsNoTracking()
                .FirstAsync(w => w.WorkspaceId == 1, ct);

            var pending = new WalletTransaction
            {
                WalletId = wallet.Id,
                Kind = WalletTransactionKind.MessageCharge,
                Status = WalletTransactionStatus.Pending,
                AmountUsd = -quote.ChargedUsd,
                BalanceAfterUsd = wallet.BalanceUsd,
                Reference = $"reserve:{Guid.NewGuid():N}",
                InitiatedByUserId = actingUserId,
                CreatedAt = nowUtc
            };
            _db.WalletTransactions.Add(pending);
            await _db.SaveChangesAsync(ct);

            return new BillingReservation(pending.Id, quote, conversation.Id, actingUserId);
        }

        public async Task CommitAsync(BillingReservation reservation, int messageId, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(reservation);

            // Free send: no wallet movement, just the audit row paired with the message.
            if (reservation.PendingTransactionId is null)
            {
                var freeRecord = BuildRecord(reservation, messageId, walletTransactionId: null);
                _db.MessageBillingRecords.Add(freeRecord);
                await _db.SaveChangesAsync(ct);

                _logger.LogInformation(
                    "[BILLING] Free message {MessageId} recorded ({Rule}, {Country}).",
                    messageId, reservation.Quote.AppliedRuleLabel, reservation.Quote.ResolvedCountryCode);
                return;
            }

            // Paid send: commit the reservation against the wallet using the same optimistic
            // concurrency pattern WalletService uses for top-ups & manual adjustments.
            for (var attempt = 1; attempt <= MaxConcurrencyRetries; attempt++)
            {
                var pending = await _db.WalletTransactions
                    .FirstOrDefaultAsync(t => t.Id == reservation.PendingTransactionId.Value, ct);

                if (pending is null)
                    throw new InvalidOperationException(
                        $"Reservation {reservation.PendingTransactionId} vanished before commit.");

                // Idempotency: re-call with same reservation id should be a no-op.
                if (pending.Status == WalletTransactionStatus.Succeeded)
                {
                    _logger.LogInformation(
                        "[BILLING] Reservation {Id} already committed for message {MessageId} — skipping.",
                        pending.Id, messageId);
                    return;
                }

                var wallet = await _db.Wallets.FirstAsync(w => w.Id == pending.WalletId, ct);
                var before = wallet.BalanceUsd;
                var after = before + pending.AmountUsd; // pending.AmountUsd is negative

                wallet.BalanceUsd = after;
                wallet.UpdatedAt = DateTime.UtcNow;

                pending.Status = WalletTransactionStatus.Succeeded;
                pending.MessageId = messageId;
                pending.BalanceAfterUsd = after;
                pending.Reference = $"msg:{messageId}";

                var record = BuildRecord(reservation, messageId, walletTransactionId: pending.Id);
                _db.MessageBillingRecords.Add(record);

                _db.BillingAuditLogs.Add(new BillingAuditLog
                {
                    Actor = $"user:{reservation.ActingUserId}",
                    Action = "wallet.debit.message",
                    EntityType = nameof(WalletTransaction),
                    EntityId = pending.Id.ToString(),
                    BeforeJson = JsonSerializer.Serialize(new { BalanceUsd = before }),
                    AfterJson  = JsonSerializer.Serialize(new
                    {
                        BalanceUsd = after,
                        AmountUsd = pending.AmountUsd,
                        MessageId = messageId,
                        Rule = reservation.Quote.AppliedRuleLabel,
                        Country = reservation.Quote.ResolvedCountryCode
                    }),
                    AtUtc = DateTime.UtcNow
                });

                try
                {
                    await _db.SaveChangesAsync(ct);
                    _logger.LogInformation(
                        "[BILLING] Committed message {MessageId}: charged {Amount:0.0000}, balance {Before:0.0000} → {After:0.0000} ({Rule}).",
                        messageId, -pending.AmountUsd, before, after, reservation.Quote.AppliedRuleLabel);
                    return;
                }
                catch (DbUpdateConcurrencyException)
                {
                    _logger.LogWarning(
                        "[BILLING] Concurrency conflict committing reservation {Id} on attempt {Attempt}/{Max}.",
                        pending.Id, attempt, MaxConcurrencyRetries);
                    if (attempt == MaxConcurrencyRetries) throw;
                    foreach (var entry in _db.ChangeTracker.Entries().ToList())
                        entry.State = EntityState.Detached;
                    await Task.Delay(50 * attempt, ct);
                }
            }

            throw new InvalidOperationException("Billing commit did not converge after retries.");
        }

        public async Task ReleaseAsync(BillingReservation reservation, string reason, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(reservation);
            if (reservation.PendingTransactionId is null) return; // nothing to release for free sends

            var pending = await _db.WalletTransactions
                .FirstOrDefaultAsync(t => t.Id == reservation.PendingTransactionId.Value, ct);
            if (pending is null) return;

            // Don't roll back a reservation that already committed (shouldn't happen, but be safe).
            if (pending.Status != WalletTransactionStatus.Pending)
            {
                _logger.LogWarning(
                    "[BILLING] Refusing to release reservation {Id} in status {Status}.",
                    pending.Id, pending.Status);
                return;
            }

            pending.Status = WalletTransactionStatus.Failed;
            pending.Reason = reason;

            _db.BillingAuditLogs.Add(new BillingAuditLog
            {
                Actor = $"user:{reservation.ActingUserId}",
                Action = "wallet.reserve.released",
                EntityType = nameof(WalletTransaction),
                EntityId = pending.Id.ToString(),
                AfterJson = JsonSerializer.Serialize(new { Reason = reason, AmountFreed = -pending.AmountUsd }),
                AtUtc = DateTime.UtcNow
            });

            await _db.SaveChangesAsync(ct);
            _logger.LogInformation(
                "[BILLING] Released reservation {Id} ({Amount:0.0000}): {Reason}",
                pending.Id, -pending.AmountUsd, reason);
        }

        private static MessageBillingRecord BuildRecord(BillingReservation r, int messageId, long? walletTransactionId)
        {
            var q = r.Quote;
            return new MessageBillingRecord
            {
                MessageId = messageId,
                Category = q.Category,
                CountryCode = q.ResolvedCountryCode,
                MetaCostUsd = q.MetaCostUsd,
                ChargedUsd = q.ChargedUsd,
                MarkupPercentageAtTime = q.MarkupPercentageAtTime,
                WasInServiceWindow = q.WasInServiceWindow,
                FreeEntryPoint = q.FreeEntryPoint,
                AppliedRuleLabel = q.AppliedRuleLabel,
                WalletTransactionId = walletTransactionId,
                CreatedAt = DateTime.UtcNow
            };
        }
    }
}
