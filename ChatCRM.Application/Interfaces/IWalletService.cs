using ChatCRM.Application.Billing.DTOs;
using ChatCRM.Domain.Entities;

namespace ChatCRM.Application.Interfaces
{
    /// <summary>
    /// Reads and (admin-driven) mutations of the workspace wallet. The phase-4 billing gate that
    /// debits per outgoing message will use the same wallet primitives but lives behind a
    /// different interface (IBillingGate) — keeps the read/admin side separated from the hot path.
    /// </summary>
    public interface IWalletService
    {
        /// <summary>Current wallet balance + recent activity for the Billing page.</summary>
        Task<BillingOverviewDto> GetOverviewAsync(int recentLimit = 10, CancellationToken cancellationToken = default);

        /// <summary>Just the balance and threshold for the topbar pill — kept lightweight.</summary>
        Task<WalletSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);

        /// <summary>Aggregate KPIs + per-day chart series for the analytics page.</summary>
        Task<BillingAnalyticsDto> GetAnalyticsAsync(
            DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default);

        /// <summary>Paged + filtered transactions log for the analytics page.</summary>
        Task<WalletTransactionLogPage> QueryTransactionsAsync(
            WalletTransactionLogQuery query, CancellationToken cancellationToken = default);

        /// <summary>
        /// Wallet balance minus the sum of currently-Pending message charges. Phase-4 billing gate
        /// uses this — never the raw balance — so two concurrent sends can't both pass the
        /// affordability check and over-draft the wallet beyond the reservation hold.
        /// </summary>
        Task<decimal> GetEffectiveBalanceAsync(CancellationToken cancellationToken = default);

        /// <summary>The wallet's Stripe Customer id, set lazily on first successful top-up. Null until then.</summary>
        Task<string?> GetStripeCustomerIdAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Apply a manual credit or debit. Admin-only — caller is responsible for the permission
        /// check. Records a WalletTransaction (Manual{Credit,Debit}) and a BillingAuditLog row,
        /// using optimistic concurrency on the Wallet rowversion. Negative balance is hard-floored
        /// at -$1.00; further debits throw <see cref="ChatCRM.Application.Billing.Exceptions.InsufficientBalanceException"/>.
        /// </summary>
        Task<WalletTransaction> ApplyManualAdjustmentAsync(
            AdjustBalanceDto dto,
            string actingUserId,
            string actingUserDisplayName,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Insert a Pending top-up transaction immediately before redirecting the user to the
        /// payment provider's hosted Checkout page. Balance is NOT credited yet — that happens
        /// in <see cref="CompleteTopUpAsync"/> when the webhook confirms payment success.
        /// </summary>
        Task<WalletTransaction> RecordTopUpRequestAsync(
            decimal amountUsd,
            string providerSessionId,
            string initiatedByUserId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Complete a previously-pending top-up: credit the wallet, mark Succeeded, save the
        /// Stripe Customer id if newly issued, write the audit log entry. Idempotent — if the
        /// row is already Succeeded the call is a no-op so duplicate webhook deliveries can't
        /// double-credit.
        /// </summary>
        /// <returns>The updated transaction, or null if no Pending row matched the session id.</returns>
        Task<WalletTransaction?> CompleteTopUpAsync(
            string providerSessionId,
            string? providerCustomerId,
            string webhookEventId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Mark a pending top-up as Failed (no balance change). Idempotent on re-delivery.
        /// </summary>
        Task<WalletTransaction?> MarkTopUpFailedAsync(
            string providerSessionId,
            string reason,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Update auto-recharge settings for the workspace wallet. Setting
        /// <paramref name="enabled"/> = true requires a previously saved payment method via
        /// SetupIntent — the caller is responsible for the prerequisite check.
        /// </summary>
        Task UpdateAutoRechargeAsync(
            bool enabled, decimal? triggerUsd, decimal? amountUsd,
            string actingUserId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Persist the default payment method id captured by a successful SetupIntent.
        /// Idempotent if the same id is set twice.
        /// </summary>
        Task SetDefaultPaymentMethodAsync(
            string paymentMethodId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Credit the wallet from a successful auto-recharge charge. Same shape as
        /// <see cref="CompleteTopUpAsync"/> but kind = AutoRechargeCharge.
        /// </summary>
        Task<WalletTransaction> RecordAutoRechargeAsync(
            decimal amountUsd, string paymentIntentId, string? chargeId,
            CancellationToken cancellationToken = default);
    }

    /// <summary>Lightweight snapshot used by the topbar pill — no allocations beyond two decimals.</summary>
    public sealed record WalletSnapshot(decimal BalanceUsd, decimal LowBalanceThresholdUsd, string DisplayCurrency, BalanceHealth Health);
}
