namespace ChatCRM.Domain.Entities
{
    /// <summary>
    /// Workspace-level billing configuration. Singleton-per-workspace (one row, WorkspaceId = 1
    /// in v1 — see Wallet's notes about the multi-tenant migration path).
    /// </summary>
    public class BillingSettings
    {
        public int Id { get; set; }

        public int WorkspaceId { get; set; } = 1;

        /// <summary>
        /// Markup applied on top of Meta's base price. Default 35%, set by business decision.
        /// Stored as percent (35.00 = 35%), not as a fraction.
        /// </summary>
        public decimal MarkupPercentage { get; set; } = 35m;

        /// <summary>ISO-4217 currency code. v1 ignores this and renders USD.</summary>
        public string Currency { get; set; } = "USD";

        /// <summary>Markdown shown on the Billing page so customers know what's refundable.</summary>
        public string RefundPolicy { get; set; } =
            "Top-up balances are non-refundable once added. Failed messages are automatically " +
            "refunded to your wallet. Manual refunds for billing errors can be requested via " +
            "support — contact us within 30 days of the charge.";

        /// <summary>Optional footer text on PDF invoices (tax line, support email, etc.).</summary>
        public string? InvoiceFooter { get; set; }

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
