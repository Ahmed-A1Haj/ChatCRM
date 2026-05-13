namespace ChatCRM.Domain.Entities
{
    /// <summary>
    /// Per-outgoing-message audit record. Pairs a <see cref="Message"/> with the snapshot of the
    /// pricing decision at send-time: which Meta rule fired, what we paid Meta, what we charged
    /// the customer, what the markup was.
    ///
    /// Even free Service-window messages get a row (MetaCostUsd = ChargedUsd = 0) so admin
    /// reports can break down volume by category without a NULL story. Paid messages also link
    /// to the <see cref="WalletTransaction"/> that recorded the actual debit; free messages
    /// have a null <c>WalletTransactionId</c> (no wallet movement happened).
    /// </summary>
    public class MessageBillingRecord
    {
        public long Id { get; set; }

        /// <summary>One billing record per outgoing message. Unique FK.</summary>
        public int MessageId { get; set; }
        public Message Message { get; set; } = null!;

        public MetaMessageCategory Category { get; set; }

        /// <summary>ISO-3166-1 alpha-2 of the recipient. "**" if no specific rule matched.</summary>
        public string CountryCode { get; set; } = "**";

        public decimal MetaCostUsd { get; set; }
        public decimal ChargedUsd { get; set; }
        public decimal MarkupPercentageAtTime { get; set; }

        public bool WasInServiceWindow { get; set; }
        public bool FreeEntryPoint { get; set; }

        /// <summary>Audit-friendly label, e.g. "Marketing/EG/2026-05-06" or "Service/SA/window".</summary>
        public string AppliedRuleLabel { get; set; } = string.Empty;

        /// <summary>Null for free messages (no wallet movement). Set for paid messages.</summary>
        public long? WalletTransactionId { get; set; }
        public WalletTransaction? WalletTransaction { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
