using System.Text.Json;
using ChatCRM.Application.Billing.DTOs;
using ChatCRM.Application.Billing.Exceptions;
using ChatCRM.Application.Interfaces;
using ChatCRM.Domain.Entities;
using ChatCRM.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ChatCRM.Infrastructure.Services
{
    /// <summary>
    /// Workspace wallet read/admin surface. Per the phase plan: optimistic concurrency on Wallet
    /// row version + 3-attempt retry on conflict. Negative-balance hard floor at -$1.00 to absorb
    /// in-flight reservations without ever drifting further. Every mutation also writes an
    /// append-only BillingAuditLog entry.
    /// </summary>
    public sealed class WalletService : IWalletService
    {
        private const int DefaultWorkspaceId = 1;
        private const decimal NegativeFloorUsd = -1.0m;
        private const int MaxConcurrencyRetries = 3;

        private readonly AppDbContext _db;
        private readonly ILogger<WalletService> _logger;

        public WalletService(AppDbContext db, ILogger<WalletService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<BillingOverviewDto> GetOverviewAsync(int recentLimit = 10, CancellationToken ct = default)
        {
            var wallet = await GetSingletonWalletAsync(track: false, ct);
            var settings = await _db.BillingSettings.AsNoTracking().FirstOrDefaultAsync(ct);

            var recent = await _db.WalletTransactions
                .AsNoTracking()
                .Where(t => t.WalletId == wallet.Id)
                .OrderByDescending(t => t.CreatedAt)
                .Take(recentLimit)
                .Select(t => new WalletTransactionDto
                {
                    Id = t.Id,
                    Kind = t.Kind,
                    Status = t.Status,
                    AmountUsd = t.AmountUsd,
                    BalanceAfterUsd = t.BalanceAfterUsd,
                    Reference = t.Reference,
                    Reason = t.Reason,
                    InitiatedByName = t.InitiatedByUser != null
                        ? (string.IsNullOrWhiteSpace(t.InitiatedByUser.FirstName)
                            ? t.InitiatedByUser.Email
                            : (t.InitiatedByUser.FirstName + " " + t.InitiatedByUser.LastName).Trim())
                        : null,
                    CreatedAt = t.CreatedAt
                })
                .ToListAsync(ct);

            return new BillingOverviewDto
            {
                BalanceUsd = wallet.BalanceUsd,
                LowBalanceThresholdUsd = wallet.LowBalanceThresholdUsd,
                DisplayCurrency = wallet.DisplayCurrency,
                Health = HealthOf(wallet.BalanceUsd, wallet.LowBalanceThresholdUsd),
                RefundPolicy = settings?.RefundPolicy ?? string.Empty,
                MarkupPercentage = settings?.MarkupPercentage ?? 35m,
                RecentTransactions = recent
            };
        }

        public async Task<string?> GetStripeCustomerIdAsync(CancellationToken ct = default)
        {
            return await _db.Wallets.AsNoTracking()
                .Where(w => w.WorkspaceId == DefaultWorkspaceId)
                .Select(w => w.StripeCustomerId)
                .FirstOrDefaultAsync(ct);
        }

        public async Task<WalletSnapshot> GetSnapshotAsync(CancellationToken ct = default)
        {
            var w = await _db.Wallets
                .AsNoTracking()
                .Where(x => x.WorkspaceId == DefaultWorkspaceId)
                .Select(x => new { x.BalanceUsd, x.LowBalanceThresholdUsd, x.DisplayCurrency })
                .FirstAsync(ct);
            return new WalletSnapshot(w.BalanceUsd, w.LowBalanceThresholdUsd, w.DisplayCurrency,
                                      HealthOf(w.BalanceUsd, w.LowBalanceThresholdUsd));
        }

        public async Task<BillingAnalyticsDto> GetAnalyticsAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct = default)
        {
            if (toUtc <= fromUtc)
                throw new InvalidOperationException("Analytics period must end after it starts.");

            var wallet = await GetSingletonWalletAsync(track: false, ct);

            // Ledger aggregates: Spent (debits), ToppedUp (credits). Pending rows are excluded —
            // analytics shows committed history only.
            var ledger = await _db.WalletTransactions
                .AsNoTracking()
                .Where(t => t.WalletId == wallet.Id
                         && t.Status == WalletTransactionStatus.Succeeded
                         && t.CreatedAt >= fromUtc && t.CreatedAt < toUtc)
                .ToListAsync(ct);

            var totalSpent = ledger
                .Where(t => t.Kind == WalletTransactionKind.MessageCharge || t.Kind == WalletTransactionKind.AutoRechargeCharge || t.Kind == WalletTransactionKind.ManualDebit)
                .Sum(t => Math.Abs(t.AmountUsd));
            var totalToppedUp = ledger
                .Where(t => t.Kind == WalletTransactionKind.TopUp || t.Kind == WalletTransactionKind.ManualCredit || t.Kind == WalletTransactionKind.MessageRefund)
                .Sum(t => t.AmountUsd);

            // MessageBillingRecord aggregates: Meta cost, charged, free vs paid counts, by-category.
            var records = await _db.MessageBillingRecords
                .AsNoTracking()
                .Where(r => r.CreatedAt >= fromUtc && r.CreatedAt < toUtc)
                .Select(r => new { r.Category, r.MetaCostUsd, r.ChargedUsd, r.CreatedAt })
                .ToListAsync(ct);

            var totalMetaCost = records.Sum(r => r.MetaCostUsd);
            var totalCharged = records.Sum(r => r.ChargedUsd);
            var paidCount = records.Count(r => r.ChargedUsd > 0m);
            var freeCount = records.Count - paidCount;

            // Daily series — densify so the chart has zero-rows on empty days for continuity.
            var daily = new List<BillingDailyPoint>();
            var spentByDay = ledger
                .Where(t => t.Kind == WalletTransactionKind.MessageCharge)
                .GroupBy(t => t.CreatedAt.Date)
                .ToDictionary(g => g.Key, g => g.Sum(x => Math.Abs(x.AmountUsd)));
            var toppedByDay = ledger
                .Where(t => t.Kind == WalletTransactionKind.TopUp)
                .GroupBy(t => t.CreatedAt.Date)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.AmountUsd));
            var paidByDay = records
                .Where(r => r.ChargedUsd > 0m)
                .GroupBy(r => r.CreatedAt.Date)
                .ToDictionary(g => g.Key, g => g.Count());

            for (var d = fromUtc.Date; d < toUtc.Date; d = d.AddDays(1))
            {
                daily.Add(new BillingDailyPoint
                {
                    DayUtc = d,
                    SpentUsd = spentByDay.TryGetValue(d, out var s) ? s : 0m,
                    ToppedUpUsd = toppedByDay.TryGetValue(d, out var u) ? u : 0m,
                    PaidMessages = paidByDay.TryGetValue(d, out var c) ? c : 0
                });
            }

            var byCategory = records
                .GroupBy(r => r.Category)
                .Select(g => new BillingCategoryBreakdown
                {
                    Category = g.Key,
                    Count = g.Count(),
                    ChargedUsd = g.Sum(x => x.ChargedUsd)
                })
                .OrderByDescending(c => c.ChargedUsd)
                .ToList();

            return new BillingAnalyticsDto
            {
                PeriodStartUtc = fromUtc,
                PeriodEndUtc = toUtc,
                TotalSpentUsd = totalSpent,
                TotalToppedUpUsd = totalToppedUp,
                TotalMetaCostUsd = totalMetaCost,
                TotalChargedUsd = totalCharged,
                PaidMessageCount = paidCount,
                FreeMessageCount = freeCount,
                Daily = daily,
                ByCategory = byCategory
            };
        }

        public async Task<WalletTransactionLogPage> QueryTransactionsAsync(WalletTransactionLogQuery query, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(query);
            var wallet = await GetSingletonWalletAsync(track: false, ct);

            var q = _db.WalletTransactions
                .AsNoTracking()
                .Where(t => t.WalletId == wallet.Id);

            if (query.FromUtc.HasValue) q = q.Where(t => t.CreatedAt >= query.FromUtc.Value);
            if (query.ToUtc.HasValue)   q = q.Where(t => t.CreatedAt < query.ToUtc.Value);
            if (query.Kind.HasValue)    q = q.Where(t => t.Kind == query.Kind.Value);
            if (query.Status.HasValue)  q = q.Where(t => t.Status == query.Status.Value);

            var total = await q.CountAsync(ct);
            var page = Math.Max(0, query.Page);
            var size = Math.Clamp(query.PageSize, 10, 100);

            var rows = await q
                .OrderByDescending(t => t.CreatedAt)
                .Skip(page * size).Take(size)
                .Select(t => new WalletTransactionDto
                {
                    Id = t.Id,
                    Kind = t.Kind,
                    Status = t.Status,
                    AmountUsd = t.AmountUsd,
                    BalanceAfterUsd = t.BalanceAfterUsd,
                    Reference = t.Reference,
                    Reason = t.Reason,
                    InitiatedByName = t.InitiatedByUser != null
                        ? (string.IsNullOrWhiteSpace(t.InitiatedByUser.FirstName)
                            ? t.InitiatedByUser.Email
                            : (t.InitiatedByUser.FirstName + " " + t.InitiatedByUser.LastName).Trim())
                        : null,
                    CreatedAt = t.CreatedAt
                })
                .ToListAsync(ct);

            return new WalletTransactionLogPage
            {
                Rows = rows,
                Page = page,
                PageSize = size,
                TotalCount = total
            };
        }

        public async Task<decimal> GetEffectiveBalanceAsync(CancellationToken ct = default)
        {
            var wallet = await GetSingletonWalletAsync(track: false, ct);

            // Pending MessageCharge rows are reservations the gate took; they haven't actually
            // moved the balance yet (BalanceAfterUsd is a placeholder). Subtract their negative
            // amounts (i.e. add their |amount|) so two concurrent sends can't both pass the check.
            var pendingHoldsUsd = await _db.WalletTransactions
                .AsNoTracking()
                .Where(t => t.WalletId == wallet.Id
                         && t.Status == WalletTransactionStatus.Pending
                         && t.Kind == WalletTransactionKind.MessageCharge)
                .SumAsync(t => t.AmountUsd, ct);

            // pendingHoldsUsd is negative (debit), so adding it shrinks the effective balance.
            return wallet.BalanceUsd + pendingHoldsUsd;
        }

        public async Task<WalletTransaction> ApplyManualAdjustmentAsync(
            AdjustBalanceDto dto, string actingUserId, string actingUserDisplayName, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(dto);
            if (dto.AmountUsd <= 0m)
                throw new InvalidOperationException("Adjustment amount must be greater than zero.");
            if (string.IsNullOrWhiteSpace(dto.Reason) || dto.Reason.Trim().Length < 5)
                throw new InvalidOperationException("A reason of at least 5 characters is required for manual adjustments.");
            if (dto.AmountUsd > 10_000m)
                throw new InvalidOperationException("Manual adjustments are capped at $10,000 per request — split into multiple if larger.");

            var signedDelta = dto.IsCredit ? dto.AmountUsd : -dto.AmountUsd;
            var kind = dto.IsCredit ? WalletTransactionKind.ManualCredit : WalletTransactionKind.ManualDebit;
            var reason = dto.Reason.Trim();

            // Optimistic-concurrency retry loop. Each attempt: re-read the wallet, mutate, save.
            // EF throws DbUpdateConcurrencyException when another writer changed RowVersion in between.
            for (var attempt = 1; attempt <= MaxConcurrencyRetries; attempt++)
            {
                var wallet = await GetSingletonWalletAsync(track: true, ct);
                var before = wallet.BalanceUsd;
                var after = before + signedDelta;

                if (after < NegativeFloorUsd)
                    throw new InsufficientBalanceException(before, Math.Abs(signedDelta));

                wallet.BalanceUsd = after;
                wallet.UpdatedAt = DateTime.UtcNow;

                var ledger = new WalletTransaction
                {
                    WalletId = wallet.Id,
                    Kind = kind,
                    Status = WalletTransactionStatus.Succeeded,
                    AmountUsd = signedDelta,
                    BalanceAfterUsd = after,
                    Reason = reason,
                    InitiatedByUserId = actingUserId,
                    CreatedAt = DateTime.UtcNow
                };
                _db.WalletTransactions.Add(ledger);

                _db.BillingAuditLogs.Add(new BillingAuditLog
                {
                    Actor = $"user:{actingUserId} ({actingUserDisplayName})",
                    Action = dto.IsCredit ? "wallet.credit.manual" : "wallet.debit.manual",
                    EntityType = nameof(Wallet),
                    EntityId = wallet.Id.ToString(),
                    BeforeJson = JsonSerializer.Serialize(new { BalanceUsd = before }),
                    AfterJson  = JsonSerializer.Serialize(new { BalanceUsd = after, Reason = reason, Amount = signedDelta }),
                    AtUtc = DateTime.UtcNow
                });

                try
                {
                    await _db.SaveChangesAsync(ct);
                    _logger.LogInformation(
                        "[WALLET] Manual {Kind} of {Amount:0.0000} by {User}; balance {Before:0.0000} → {After:0.0000}.",
                        kind, signedDelta, actingUserId, before, after);
                    return ledger;
                }
                catch (DbUpdateConcurrencyException)
                {
                    _logger.LogWarning("[WALLET] Concurrency conflict on attempt {Attempt}/{Max}; retrying.", attempt, MaxConcurrencyRetries);
                    if (attempt == MaxConcurrencyRetries) throw;
                    // Detach so next iteration re-reads fresh.
                    foreach (var entry in _db.ChangeTracker.Entries().ToList())
                        entry.State = EntityState.Detached;
                    await Task.Delay(50 * attempt, ct);   // 50ms, 100ms, 150ms — small linear backoff
                }
            }

            throw new InvalidOperationException("Wallet update did not converge after retries.");
        }

        // ── Top-up lifecycle (phase 3) ───────────────────────────────────────

        public async Task<WalletTransaction> RecordTopUpRequestAsync(
            decimal amountUsd, string providerSessionId, string initiatedByUserId, CancellationToken ct = default)
        {
            if (amountUsd <= 0m) throw new InvalidOperationException("Top-up amount must be positive.");
            if (string.IsNullOrWhiteSpace(providerSessionId)) throw new InvalidOperationException("Session id required.");

            var wallet = await GetSingletonWalletAsync(track: false, ct);

            var pending = new WalletTransaction
            {
                WalletId = wallet.Id,
                Kind = WalletTransactionKind.TopUp,
                Status = WalletTransactionStatus.Pending,
                AmountUsd = amountUsd,
                BalanceAfterUsd = wallet.BalanceUsd,    // placeholder, overwritten on Succeeded
                Reference = providerSessionId,
                InitiatedByUserId = initiatedByUserId,
                CreatedAt = DateTime.UtcNow
            };
            _db.WalletTransactions.Add(pending);

            _db.BillingAuditLogs.Add(new BillingAuditLog
            {
                Actor = $"user:{initiatedByUserId}",
                Action = "topup.requested",
                EntityType = nameof(WalletTransaction),
                EntityId = providerSessionId,
                AfterJson = JsonSerializer.Serialize(new { AmountUsd = amountUsd, Provider = "stripe" }),
                AtUtc = DateTime.UtcNow
            });

            await _db.SaveChangesAsync(ct);
            return pending;
        }

        public async Task<WalletTransaction?> CompleteTopUpAsync(
            string providerSessionId, string? providerCustomerId, string webhookEventId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(providerSessionId)) return null;

            for (var attempt = 1; attempt <= MaxConcurrencyRetries; attempt++)
            {
                // Re-read on each attempt so concurrency-conflict retries see fresh state.
                var pending = await _db.WalletTransactions
                    .FirstOrDefaultAsync(t => t.Reference == providerSessionId && t.Kind == WalletTransactionKind.TopUp, ct);

                if (pending is null)
                {
                    _logger.LogWarning("[TOPUP] Webhook {Event} referenced session {Session} but no pending row exists.",
                        webhookEventId, providerSessionId);
                    return null;
                }

                // Idempotency: a re-delivered "checkout.session.completed" finds the row already
                // Succeeded. Don't credit again.
                if (pending.Status == WalletTransactionStatus.Succeeded)
                {
                    _logger.LogInformation("[TOPUP] Session {Session} already completed — webhook {Event} ignored.",
                        providerSessionId, webhookEventId);
                    return pending;
                }
                if (pending.Status == WalletTransactionStatus.Failed)
                {
                    _logger.LogWarning("[TOPUP] Session {Session} already marked Failed; refusing to complete.", providerSessionId);
                    return pending;
                }

                var wallet = await _db.Wallets.FirstAsync(w => w.Id == pending.WalletId, ct);
                var before = wallet.BalanceUsd;
                var after = before + pending.AmountUsd;

                wallet.BalanceUsd = after;
                wallet.UpdatedAt = DateTime.UtcNow;

                // Lazy Stripe Customer attach — first successful top-up captures the customer id.
                if (string.IsNullOrWhiteSpace(wallet.StripeCustomerId) && !string.IsNullOrWhiteSpace(providerCustomerId))
                    wallet.StripeCustomerId = providerCustomerId;

                pending.Status = WalletTransactionStatus.Succeeded;
                pending.BalanceAfterUsd = after;

                _db.BillingAuditLogs.Add(new BillingAuditLog
                {
                    Actor = $"system:stripe-webhook ({webhookEventId})",
                    Action = "topup.succeeded",
                    EntityType = nameof(WalletTransaction),
                    EntityId = providerSessionId,
                    BeforeJson = JsonSerializer.Serialize(new { BalanceUsd = before }),
                    AfterJson  = JsonSerializer.Serialize(new { BalanceUsd = after, AmountCredited = pending.AmountUsd, StripeCustomerId = wallet.StripeCustomerId }),
                    AtUtc = DateTime.UtcNow
                });

                try
                {
                    await _db.SaveChangesAsync(ct);
                    _logger.LogInformation("[TOPUP] Session {Session} completed — credited {Amount:0.0000}; balance {Before:0.0000} → {After:0.0000}.",
                        providerSessionId, pending.AmountUsd, before, after);
                    return pending;
                }
                catch (DbUpdateConcurrencyException)
                {
                    _logger.LogWarning("[TOPUP] Concurrency conflict on attempt {Attempt}/{Max} completing session {Session}.",
                        attempt, MaxConcurrencyRetries, providerSessionId);
                    if (attempt == MaxConcurrencyRetries) throw;
                    foreach (var entry in _db.ChangeTracker.Entries().ToList())
                        entry.State = EntityState.Detached;
                    await Task.Delay(50 * attempt, ct);
                }
            }

            throw new InvalidOperationException("Top-up completion did not converge after retries.");
        }

        public async Task<WalletTransaction?> MarkTopUpFailedAsync(string providerSessionId, string reason, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(providerSessionId)) return null;

            var pending = await _db.WalletTransactions
                .FirstOrDefaultAsync(t => t.Reference == providerSessionId && t.Kind == WalletTransactionKind.TopUp, ct);
            if (pending is null) return null;

            if (pending.Status == WalletTransactionStatus.Failed) return pending; // idempotent
            if (pending.Status == WalletTransactionStatus.Succeeded)
            {
                _logger.LogWarning("[TOPUP] Refusing to mark already-Succeeded session {Session} as Failed.", providerSessionId);
                return pending;
            }

            pending.Status = WalletTransactionStatus.Failed;
            pending.Reason = reason;

            _db.BillingAuditLogs.Add(new BillingAuditLog
            {
                Actor = "system:stripe-webhook",
                Action = "topup.failed",
                EntityType = nameof(WalletTransaction),
                EntityId = providerSessionId,
                AfterJson = JsonSerializer.Serialize(new { Reason = reason }),
                AtUtc = DateTime.UtcNow
            });

            await _db.SaveChangesAsync(ct);
            return pending;
        }

        // ── Auto-recharge (phase 11) ─────────────────────────────────────────

        public async Task UpdateAutoRechargeAsync(
            bool enabled, decimal? triggerUsd, decimal? amountUsd,
            string actingUserId, CancellationToken ct = default)
        {
            if (enabled)
            {
                if (!triggerUsd.HasValue || triggerUsd.Value <= 0m)
                    throw new InvalidOperationException("Trigger threshold must be > 0 to enable auto-recharge.");
                if (!amountUsd.HasValue || amountUsd.Value <= 0m)
                    throw new InvalidOperationException("Recharge amount must be > 0 to enable auto-recharge.");
                if (amountUsd.Value > 1_000m)
                    throw new InvalidOperationException("Recharge amount exceeds the per-charge $1000 cap.");
            }

            var wallet = await GetSingletonWalletAsync(track: true, ct);
            if (enabled && string.IsNullOrEmpty(wallet.DefaultPaymentMethodId))
                throw new InvalidOperationException("Save a payment method before enabling auto-recharge.");

            var before = JsonSerializer.Serialize(new
            {
                wallet.AutoRechargeEnabled, wallet.AutoRechargeTriggerUsd, wallet.AutoRechargeAmountUsd
            });

            wallet.AutoRechargeEnabled = enabled;
            wallet.AutoRechargeTriggerUsd = enabled ? triggerUsd : null;
            wallet.AutoRechargeAmountUsd = enabled ? amountUsd : null;
            wallet.UpdatedAt = DateTime.UtcNow;

            _db.BillingAuditLogs.Add(new BillingAuditLog
            {
                Actor = $"user:{actingUserId}",
                Action = enabled ? "wallet.autorecharge.enabled" : "wallet.autorecharge.disabled",
                EntityType = nameof(Wallet),
                EntityId = wallet.Id.ToString(),
                BeforeJson = before,
                AfterJson = JsonSerializer.Serialize(new { enabled, triggerUsd, amountUsd }),
                AtUtc = DateTime.UtcNow
            });

            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("[WALLET] Auto-recharge {State} (trigger {Trigger}, amount {Amount}) by {User}.",
                enabled ? "ENABLED" : "DISABLED", triggerUsd, amountUsd, actingUserId);
        }

        public async Task SetDefaultPaymentMethodAsync(string paymentMethodId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(paymentMethodId)) return;
            var wallet = await GetSingletonWalletAsync(track: true, ct);

            if (string.Equals(wallet.DefaultPaymentMethodId, paymentMethodId, StringComparison.Ordinal))
                return; // idempotent — already set

            var before = wallet.DefaultPaymentMethodId;
            wallet.DefaultPaymentMethodId = paymentMethodId;
            wallet.UpdatedAt = DateTime.UtcNow;

            _db.BillingAuditLogs.Add(new BillingAuditLog
            {
                Actor = "system:setup-intent",
                Action = "wallet.payment-method.set",
                EntityType = nameof(Wallet),
                EntityId = wallet.Id.ToString(),
                BeforeJson = JsonSerializer.Serialize(new { Previous = before }),
                AfterJson = JsonSerializer.Serialize(new { New = paymentMethodId }),
                AtUtc = DateTime.UtcNow
            });

            await _db.SaveChangesAsync(ct);
        }

        public async Task<WalletTransaction> RecordAutoRechargeAsync(
            decimal amountUsd, string paymentIntentId, string? chargeId, CancellationToken ct = default)
        {
            if (amountUsd <= 0m) throw new InvalidOperationException("Amount must be positive.");
            if (string.IsNullOrWhiteSpace(paymentIntentId)) throw new InvalidOperationException("PaymentIntent id required.");

            for (var attempt = 1; attempt <= MaxConcurrencyRetries; attempt++)
            {
                // Idempotency: check whether we already recorded this PaymentIntent. Auto-recharge
                // uses the PI id as the Reference so duplicates are blocked.
                var existing = await _db.WalletTransactions
                    .FirstOrDefaultAsync(t => t.Reference == paymentIntentId
                                           && t.Kind == WalletTransactionKind.AutoRechargeCharge, ct);
                if (existing is not null) return existing;

                var wallet = await GetSingletonWalletAsync(track: true, ct);
                var before = wallet.BalanceUsd;
                var after = before + amountUsd;

                wallet.BalanceUsd = after;
                wallet.UpdatedAt = DateTime.UtcNow;

                var entry = new WalletTransaction
                {
                    WalletId = wallet.Id,
                    Kind = WalletTransactionKind.AutoRechargeCharge,
                    Status = WalletTransactionStatus.Succeeded,
                    AmountUsd = amountUsd,
                    BalanceAfterUsd = after,
                    Reference = paymentIntentId,
                    Reason = chargeId,
                    CreatedAt = DateTime.UtcNow
                };
                _db.WalletTransactions.Add(entry);

                _db.BillingAuditLogs.Add(new BillingAuditLog
                {
                    Actor = "system:auto-recharge",
                    Action = "wallet.autorecharge.charged",
                    EntityType = nameof(WalletTransaction),
                    EntityId = paymentIntentId,
                    BeforeJson = JsonSerializer.Serialize(new { BalanceUsd = before }),
                    AfterJson = JsonSerializer.Serialize(new { BalanceUsd = after, AmountUsd = amountUsd, ChargeId = chargeId }),
                    AtUtc = DateTime.UtcNow
                });

                try
                {
                    await _db.SaveChangesAsync(ct);
                    _logger.LogInformation("[WALLET] Auto-recharge credited {Amount}: balance {Before:0.0000} → {After:0.0000}.",
                        amountUsd, before, after);
                    return entry;
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (attempt == MaxConcurrencyRetries) throw;
                    foreach (var e in _db.ChangeTracker.Entries().ToList()) e.State = EntityState.Detached;
                    await Task.Delay(50 * attempt, ct);
                }
            }

            throw new InvalidOperationException("Auto-recharge credit did not converge after retries.");
        }

        // ── helpers ──────────────────────────────────────────────────────────

        private async Task<Wallet> GetSingletonWalletAsync(bool track, CancellationToken ct)
        {
            var query = track ? _db.Wallets : _db.Wallets.AsNoTracking();
            var w = await query.FirstOrDefaultAsync(x => x.WorkspaceId == DefaultWorkspaceId, ct);
            return w ?? throw new InvalidOperationException(
                "Wallet for default workspace not found — BillingSeeder didn't run.");
        }

        private static BalanceHealth HealthOf(decimal balance, decimal threshold)
        {
            if (balance < 0m) return BalanceHealth.Negative;
            if (balance == 0m) return BalanceHealth.Empty;
            if (balance < threshold) return BalanceHealth.Low;
            return BalanceHealth.Healthy;
        }
    }
}
