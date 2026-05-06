using ChatCRM.Application.Billing.DTOs;
using ChatCRM.Domain.Entities;

namespace ChatCRM.Application.Interfaces
{
    /// <summary>
    /// Read-only pricing engine. Given the context of a message we're about to send, returns
    /// what Meta will charge us and what we'll charge the customer (Meta + markup). Pure-ish:
    /// reads only, never mutates wallet/audit state. Phase 4 wires this into the send pipeline.
    /// </summary>
    public interface IPricingService
    {
        /// <summary>
        /// Quote the cost of sending one message.
        /// </summary>
        /// <param name="category">Meta's category for the conversation/message.</param>
        /// <param name="countryCodeAlpha2">ISO-3166-1 alpha-2 country code of the recipient. Null/empty falls back to the "**" default rule.</param>
        /// <param name="inServiceWindow">True if the recipient is inside the 24h customer-service window. When category is Service AND this is true, the message is free.</param>
        /// <param name="freeEntryPoint">True if the conversation originated from a free-entry-point (FB/IG ad). Free for 72h regardless of category.</param>
        /// <param name="atUtc">Quote-time. Determines which versioned pricing rule applies (grandfathering).</param>
        Task<PricingQuote> GetQuoteAsync(
            MetaMessageCategory category,
            string? countryCodeAlpha2,
            bool inServiceWindow,
            bool freeEntryPoint,
            DateTime atUtc,
            CancellationToken cancellationToken = default);
    }
}
