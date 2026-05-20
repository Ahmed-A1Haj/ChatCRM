using ChatCRM.Domain.Entities;

namespace ChatCRM.Application.Billing.DTOs
{
    /// <summary>
    /// Result of asking the pricing engine "how much would it cost to send this message?".
    /// All amounts are USD. Quotes are deterministic snapshots of state at quote-time —
    /// store them on MessageBillingRecord so future audits show exactly what we charged
    /// and why, even if Meta's rates or our markup change later.
    /// </summary>
    public sealed record PricingQuote
    {
        public required MetaMessageCategory Category { get; init; }

        /// <summary>The country we resolved against (may be "**" if no specific match).</summary>
        public required string ResolvedCountryCode { get; init; }

        /// <summary>What Meta will bill us. Free in service window or for free-entry-point conversations.</summary>
        public required decimal MetaCostUsd { get; init; }

        /// <summary>Markup percentage applied at quote-time. 35.00 = 35%.</summary>
        public required decimal MarkupPercentageAtTime { get; init; }

        /// <summary>What the customer's wallet will be debited. <c>MetaCostUsd × (1 + Markup/100)</c>, rounded.</summary>
        public required decimal ChargedUsd { get; init; }

        /// <summary>Convenience flag — true when ChargedUsd is zero (service-window or free-entry-point).</summary>
        public bool IsFree => ChargedUsd == 0m;

        /// <summary>True when the recipient is inside the 24h customer-service window at quote-time.</summary>
        public required bool WasInServiceWindow { get; init; }

        /// <summary>True when the conversation is flagged as a free-entry-point (FB/IG ad-driven).</summary>
        public required bool FreeEntryPoint { get; init; }

        /// <summary>
        /// Audit-friendly label of the pricing rule that was used, e.g.
        /// <c>"Marketing/EG/2026-05-06"</c>. Useful in admin reports + receipts.
        /// </summary>
        public required string AppliedRuleLabel { get; init; }
    }
}
