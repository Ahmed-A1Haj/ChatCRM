namespace ChatCRM.Domain.Entities
{
    /// <summary>
    /// Append-only audit trail for any billing-related state change — wallet mutations, pricing
    /// rule edits, refunds issued, settings updated. <see cref="Actor"/> is a free-text string so
    /// system-driven entries (Stripe webhook, scheduled jobs) can identify themselves without an
    /// FK to Users. Compliance-grade: never deleted.
    /// </summary>
    public class BillingAuditLog
    {
        public long Id { get; set; }

        /// <summary>"user:{id}" for human actors, "system:{source}" for automated ones.</summary>
        public string Actor { get; set; } = "system:unknown";

        /// <summary>Domain action verb, e.g. "wallet.credit.manual", "topup.succeeded", "pricing.rule.updated".</summary>
        public string Action { get; set; } = string.Empty;

        public string EntityType { get; set; } = string.Empty;
        public string EntityId   { get; set; } = string.Empty;

        public string? BeforeJson { get; set; }
        public string? AfterJson  { get; set; }

        public DateTime AtUtc { get; set; } = DateTime.UtcNow;
    }
}
