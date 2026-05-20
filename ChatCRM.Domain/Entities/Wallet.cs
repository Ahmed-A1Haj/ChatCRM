namespace ChatCRM.Domain.Entities
{
    /// <summary>
    /// Per-workspace funds for paying Meta-billed WhatsApp Cloud API messages. Today there's one
    /// row, identified by <see cref="WorkspaceId"/> = 1, because ChatCRM is single-workspace.
    /// When multi-tenancy ships, every existing row gets attributed to that seed workspace and
    /// new workspaces each get their own.
    /// </summary>
    public class Wallet
    {
        public int Id { get; set; }

        /// <summary>
        /// Synthetic workspace identifier. Always 1 in v1. Indexed + unique so the singleton
        /// invariant is enforced at the DB level.
        /// </summary>
        public int WorkspaceId { get; set; } = 1;

        /// <summary>Authoritative balance in USD. Mutated via WalletTransactions writes only (Phase 2+).</summary>
        public decimal BalanceUsd { get; set; } = 0m;

        /// <summary>
        /// Currency the dashboard SHOULD display this wallet in. v1 ignores this and renders USD;
        /// the column lives now so we don't need a schema migration when EGP/SAR display lands.
        /// </summary>
        public string DisplayCurrency { get; set; } = "USD";

        /// <summary>
        /// Below this balance, in-app + email warnings fire. Default $10 (per business decision —
        /// $5 was too low, agents would hit zero mid-conversation).
        /// </summary>
        public decimal LowBalanceThresholdUsd { get; set; } = 10m;

        /// <summary>Auto-recharge wiring lives here for Phase 11. v1 leaves these unused.</summary>
        public bool AutoRechargeEnabled { get; set; } = false;
        public decimal? AutoRechargeAmountUsd { get; set; }
        public decimal? AutoRechargeTriggerUsd { get; set; }

        /// <summary>
        /// Stripe Customer id. Created lazily on first successful top-up so we don't pollute Stripe
        /// with ghost customers for users who never pay.
        /// </summary>
        public string? StripeCustomerId { get; set; }

        /// <summary>Stripe PaymentMethod id used for auto-recharge. Set by Phase 11.</summary>
        public string? DefaultPaymentMethodId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Optimistic-concurrency token. Every wallet UPDATE checks-and-bumps this so two
        /// concurrent deductions can't both succeed against the same starting balance.
        /// </summary>
        public byte[] RowVersion { get; set; } = Array.Empty<byte>();
    }
}
