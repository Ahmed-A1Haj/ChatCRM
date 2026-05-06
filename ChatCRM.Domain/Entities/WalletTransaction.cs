namespace ChatCRM.Domain.Entities
{
    /// <summary>What kind of wallet movement this row represents.</summary>
    public enum WalletTransactionKind : byte
    {
        TopUp                = 0,  // Successful Stripe Checkout
        MessageCharge        = 1,  // Outbound Cloud-API message debit
        MessageRefund        = 2,  // Refund of a failed/rejected message charge
        ManualCredit         = 3,  // Admin-initiated credit (promo, dispute, error correction)
        ManualDebit          = 4,  // Admin-initiated debit (almost never — for recovering wrongful credit)
        AutoRechargeCharge   = 5   // Phase 11: scheduled recharge from saved payment method
    }

    /// <summary>Lifecycle of a wallet transaction.</summary>
    public enum WalletTransactionStatus : byte
    {
        Pending   = 0,  // Reserved (top-up Checkout open, message-charge in flight)
        Succeeded = 1,
        Failed    = 2,
        Refunded  = 3
    }

    /// <summary>
    /// Append-only ledger of every credit and debit to a wallet. Never updated or deleted —
    /// corrections are new entries with explanatory <see cref="Reason"/>. <see cref="BalanceAfterUsd"/>
    /// is a snapshot taken at write-time so audits can reconstruct history without replaying.
    /// </summary>
    public class WalletTransaction
    {
        public long Id { get; set; }

        public int WalletId { get; set; }
        public Wallet Wallet { get; set; } = null!;

        public WalletTransactionKind Kind { get; set; }

        /// <summary>Signed: positive for credits, negative for debits. Stored at 4dp like the wallet.</summary>
        public decimal AmountUsd { get; set; }

        /// <summary>Wallet balance immediately after this row was written. Auditing safety net.</summary>
        public decimal BalanceAfterUsd { get; set; }

        public WalletTransactionStatus Status { get; set; } = WalletTransactionStatus.Succeeded;

        /// <summary>Stripe session/charge id, our MessageBillingRecord id, etc.</summary>
        public string? Reference { get; set; }

        /// <summary>Optional FK back to the message that generated this charge/refund. Null for top-ups.</summary>
        public int? MessageId { get; set; }
        public Message? Message { get; set; }

        /// <summary>Who triggered this. Null for system-driven entries (Stripe webhook, scheduled jobs).</summary>
        public string? InitiatedByUserId { get; set; }
        public User? InitiatedByUser { get; set; }

        /// <summary>Required for ManualCredit/ManualDebit. Free text up to 500 chars.</summary>
        public string? Reason { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
