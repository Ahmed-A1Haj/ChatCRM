using ChatCRM.Application.Interfaces;
using ChatCRM.Domain.Entities;
using ChatCRM.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ChatCRM.Infrastructure.Services.Payments
{
    /// <summary>
    /// Background worker that triggers wallet auto-recharge when the balance falls below the
    /// per-wallet trigger threshold. Ticks every 5 minutes; uses Stripe off-session charges
    /// against the saved default payment method.
    ///
    /// Failure handling: if a charge fails (declined, expired card, requires authentication),
    /// the worker disables auto-recharge on that wallet to avoid spamming the customer with
    /// failed charge attempts. The Stripe webhook for <c>payment_intent.payment_failed</c>
    /// would be a complementary signal but isn't required — the off-session call throws
    /// synchronously so we catch it here.
    /// </summary>
    public sealed class AutoRechargeWorker : BackgroundService
    {
        private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(5);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<AutoRechargeWorker> _logger;

        public AutoRechargeWorker(IServiceScopeFactory scopeFactory, ILogger<AutoRechargeWorker> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Stagger startup so a fresh boot doesn't slam Stripe immediately after migrations.
            try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
            catch (OperationCanceledException) { return; }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await TickAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[AUTO-RECHARGE] Tick failed; will retry next interval.");
                }

                try { await Task.Delay(TickInterval, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }

        private async Task TickAsync(CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();
            var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<AppDbContext>();
            var payment = sp.GetRequiredService<IPaymentProvider>();
            var wallet = sp.GetRequiredService<IWalletService>();

            if (!payment.IsConfigured) return;

            // Find every wallet that's enabled, has a saved PM, and is below its trigger.
            var due = await db.Wallets
                .Where(w => w.AutoRechargeEnabled
                         && w.DefaultPaymentMethodId != null
                         && w.StripeCustomerId != null
                         && w.AutoRechargeTriggerUsd != null
                         && w.AutoRechargeAmountUsd != null
                         && w.BalanceUsd < w.AutoRechargeTriggerUsd)
                .ToListAsync(ct);

            if (due.Count == 0) return;

            foreach (var w in due)
            {
                if (ct.IsCancellationRequested) break;
                await TryRechargeAsync(w, payment, wallet, db, ct);
            }
        }

        private async Task TryRechargeAsync(Wallet w, IPaymentProvider payment, IWalletService wallet, AppDbContext db, CancellationToken ct)
        {
            // Skip if a recent successful auto-recharge happened in the last 30 minutes — covers
            // scenarios where the cron runs faster than the webhook + tick interval drift.
            var sinceUtc = DateTime.UtcNow.AddMinutes(-30);
            var recent = await db.WalletTransactions
                .AnyAsync(t => t.WalletId == w.Id
                            && t.Kind == WalletTransactionKind.AutoRechargeCharge
                            && t.Status == WalletTransactionStatus.Succeeded
                            && t.CreatedAt > sinceUtc, ct);
            if (recent)
            {
                _logger.LogInformation("[AUTO-RECHARGE] Wallet {Id} recently recharged — skipping.", w.Id);
                return;
            }

            var amount = w.AutoRechargeAmountUsd!.Value;
            var idempotencyKey = $"auto-recharge:{w.Id}:{DateTime.UtcNow:yyyyMMddHHmm}";

            try
            {
                var result = await payment.ChargeSavedPaymentMethodAsync(
                    w.StripeCustomerId!, w.DefaultPaymentMethodId!, amount,
                    new Dictionary<string, string>
                    {
                        ["walletId"] = w.Id.ToString(),
                        ["kind"] = "auto-recharge"
                    },
                    idempotencyKey,
                    ct);

                await wallet.RecordAutoRechargeAsync(result.AmountUsd, result.PaymentIntentId, result.ChargeId, ct);
                _logger.LogInformation("[AUTO-RECHARGE] Wallet {Id} recharged {Amount} (pi={Pi}).", w.Id, amount, result.PaymentIntentId);
            }
            catch (Exception ex)
            {
                // Most Stripe failures (card_declined, authentication_required, …) come back as
                // StripeException with a code. We can't reliably differentiate retryable vs
                // permanent without parsing the error payload — v1: disable auto-recharge so
                // the customer notices and re-saves a card. They can re-enable from the page.
                _logger.LogError(ex, "[AUTO-RECHARGE] Charge failed for wallet {Id}; disabling auto-recharge.", w.Id);
                w.AutoRechargeEnabled = false;
                w.UpdatedAt = DateTime.UtcNow;
                db.BillingAuditLogs.Add(new BillingAuditLog
                {
                    Actor = "system:auto-recharge",
                    Action = "wallet.autorecharge.disabled",
                    EntityType = nameof(Wallet),
                    EntityId = w.Id.ToString(),
                    AfterJson = System.Text.Json.JsonSerializer.Serialize(new { Reason = "charge_failed", Error = ex.Message }),
                    AtUtc = DateTime.UtcNow
                });
                try { await db.SaveChangesAsync(ct); }
                catch (DbUpdateConcurrencyException) { /* swallowed — next tick will re-evaluate */ }
            }
        }
    }
}
