using ChatCRM.Application.Billing.DTOs;
using ChatCRM.Application.Interfaces;
using ChatCRM.Domain.Entities;
using ChatCRM.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ChatCRM.Infrastructure.Services
{
    /// <summary>
    /// Resolves the active <see cref="MetaPricingRule"/> for a (category, country, time) tuple
    /// and applies the workspace's markup percentage. Read-only — never mutates state. Wallet
    /// debits are the billing-gate's job (phase 4), not this service's.
    /// </summary>
    public sealed class PricingService : IPricingService
    {
        private const string DefaultCountry = "**";

        private readonly AppDbContext _db;
        private readonly ILogger<PricingService> _logger;

        public PricingService(AppDbContext db, ILogger<PricingService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<PricingQuote> GetQuoteAsync(
            MetaMessageCategory category,
            string? countryCodeAlpha2,
            bool inServiceWindow,
            bool freeEntryPoint,
            DateTime atUtc,
            CancellationToken cancellationToken = default)
        {
            var country = NormalizeCountry(countryCodeAlpha2);
            var markup  = await GetMarkupPercentageAsync(cancellationToken);

            // Free fast-paths — still return a structured quote so downstream code can record
            // a MessageBillingRecord with $0 amounts (full audit trail, no wallet movement).
            if (freeEntryPoint)
            {
                return new PricingQuote
                {
                    Category = category,
                    ResolvedCountryCode = country,
                    MetaCostUsd = 0m,
                    MarkupPercentageAtTime = markup,
                    ChargedUsd = 0m,
                    WasInServiceWindow = inServiceWindow,
                    FreeEntryPoint = true,
                    AppliedRuleLabel = $"FreeEntryPoint/{country}"
                };
            }

            if (category == MetaMessageCategory.Service && inServiceWindow)
            {
                return new PricingQuote
                {
                    Category = category,
                    ResolvedCountryCode = country,
                    MetaCostUsd = 0m,
                    MarkupPercentageAtTime = markup,
                    ChargedUsd = 0m,
                    WasInServiceWindow = true,
                    FreeEntryPoint = false,
                    AppliedRuleLabel = $"Service/{country}/window"
                };
            }

            // Look up: specific country first, fall back to "**" default. Versioned by EffectiveFrom/To.
            var rule = await FindActiveRuleAsync(category, country, atUtc, cancellationToken)
                       ?? (country != DefaultCountry
                            ? await FindActiveRuleAsync(category, DefaultCountry, atUtc, cancellationToken)
                            : null);

            if (rule is null)
            {
                // No rule at all — log and return a zero-quote rather than crash. The billing gate
                // can decide whether to allow zero-cost sends through (probably not — but that's a
                // policy decision, not the pricing engine's).
                _logger.LogError(
                    "No pricing rule found for category={Category} country={Country} at={AtUtc}. " +
                    "Seed missed a row — admin should add one. Returning $0 quote as a fail-safe.",
                    category, country, atUtc);

                return new PricingQuote
                {
                    Category = category,
                    ResolvedCountryCode = country,
                    MetaCostUsd = 0m,
                    MarkupPercentageAtTime = markup,
                    ChargedUsd = 0m,
                    WasInServiceWindow = inServiceWindow,
                    FreeEntryPoint = false,
                    AppliedRuleLabel = $"MISSING_RULE/{category}/{country}"
                };
            }

            var charged = Round(rule.BasePriceUsd * (1m + markup / 100m));

            return new PricingQuote
            {
                Category = category,
                ResolvedCountryCode = rule.CountryCode,
                MetaCostUsd = rule.BasePriceUsd,
                MarkupPercentageAtTime = markup,
                ChargedUsd = charged,
                WasInServiceWindow = inServiceWindow,
                FreeEntryPoint = false,
                AppliedRuleLabel = $"{category}/{rule.CountryCode}/{rule.EffectiveFrom:yyyy-MM-dd}"
            };
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private async Task<MetaPricingRule?> FindActiveRuleAsync(
            MetaMessageCategory category, string country, DateTime atUtc, CancellationToken ct)
        {
            return await _db.MetaPricingRules
                .AsNoTracking()
                .Where(r => r.Category == category
                         && r.CountryCode == country
                         && r.EffectiveFrom <= atUtc
                         && (r.EffectiveTo == null || r.EffectiveTo > atUtc))
                .OrderByDescending(r => r.EffectiveFrom)
                .FirstOrDefaultAsync(ct);
        }

        private async Task<decimal> GetMarkupPercentageAsync(CancellationToken ct)
        {
            var settings = await _db.BillingSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(ct);
            return settings?.MarkupPercentage ?? 35m;
        }

        private static string NormalizeCountry(string? countryCodeAlpha2)
        {
            if (string.IsNullOrWhiteSpace(countryCodeAlpha2)) return DefaultCountry;
            var trimmed = countryCodeAlpha2.Trim().ToUpperInvariant();
            return trimmed.Length == 2 ? trimmed : DefaultCountry;
        }

        // 4dp matches the wallet's currency precision. Decimal banker's rounding is the right
        // default here — tiny biases from cumulative rounding don't accumulate in our favor or
        // against the customer.
        private static decimal Round(decimal value)
            => Math.Round(value, 4, MidpointRounding.ToEven);
    }
}
