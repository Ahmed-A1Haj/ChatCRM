namespace ChatCRM.Domain.Entities
{
    /// <summary>
    /// One row in the per-category × per-country pricing table. Versioned via
    /// <see cref="EffectiveFrom"/> / <see cref="EffectiveTo"/> so historical
    /// MessageBillingRecords always reflect the rate at the time of charge,
    /// even after Meta updates prices. Never mutated in place — to update a
    /// rate, the admin closes the existing row (sets EffectiveTo = now) and
    /// inserts a new row with EffectiveFrom = now.
    /// </summary>
    public class MetaPricingRule
    {
        public int Id { get; set; }

        public MetaMessageCategory Category { get; set; }

        /// <summary>
        /// ISO-3166-1 alpha-2 (e.g. "US", "EG", "SA"). The literal string "**" is the
        /// catch-all for any country without a specific rule — must always exist per
        /// category so the resolver never returns null for a known category.
        /// </summary>
        public string CountryCode { get; set; } = "**";

        /// <summary>Meta's published rate in USD. Stored at 6dp for sub-cent accuracy.</summary>
        public decimal BasePriceUsd { get; set; }

        public DateTime EffectiveFrom { get; set; } = DateTime.UtcNow;
        public DateTime? EffectiveTo   { get; set; }

        /// <summary>Free-text source/reference (e.g. "Meta 2025-07 update"). Helps the audit log.</summary>
        public string? Source { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
