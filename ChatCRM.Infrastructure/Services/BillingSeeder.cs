using System.Text.Json;
using System.Text.Json.Serialization;
using ChatCRM.Domain.Entities;
using ChatCRM.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ChatCRM.Infrastructure.Services
{
    /// <summary>
    /// First-run seeder for the billing system. Idempotent — running it on a populated DB is a
    /// no-op. Seeds:
    ///   • Singleton wallet for WorkspaceId = 1
    ///   • Singleton BillingSettings (markup, refund policy, currency)
    ///   • Initial Meta pricing rules from <c>Resources/billing/meta-pricing.json</c>
    /// </summary>
    public static class BillingSeeder
    {
        public const int DefaultWorkspaceId = 1;

        public static async Task SeedAsync(
            AppDbContext db,
            ILogger logger,
            string? pricingRulesJsonPath = null,
            CancellationToken cancellationToken = default)
        {
            await SeedWalletAsync(db, logger, cancellationToken);
            await SeedBillingSettingsAsync(db, logger, cancellationToken);
            await SeedPricingRulesAsync(db, logger, pricingRulesJsonPath, cancellationToken);
        }

        // ── Wallet (phase 0) ─────────────────────────────────────────────

        private static async Task SeedWalletAsync(AppDbContext db, ILogger logger, CancellationToken ct)
        {
            var existing = await db.Wallets
                .AsNoTracking()
                .FirstOrDefaultAsync(w => w.WorkspaceId == DefaultWorkspaceId, ct);
            if (existing is not null) return;

            db.Wallets.Add(new Wallet
            {
                WorkspaceId = DefaultWorkspaceId,
                BalanceUsd = 0m,
                DisplayCurrency = "USD",
                LowBalanceThresholdUsd = 10m,
                AutoRechargeEnabled = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            try
            {
                await db.SaveChangesAsync(ct);
                logger.LogInformation("[SEED] Created singleton wallet for WorkspaceId={WorkspaceId} with $0 balance.", DefaultWorkspaceId);
            }
            catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
            {
                logger.LogInformation("[SEED] Wallet already exists (concurrent seeder won the race) — continuing.");
            }
        }

        // ── BillingSettings (phase 1) ────────────────────────────────────

        private static async Task SeedBillingSettingsAsync(AppDbContext db, ILogger logger, CancellationToken ct)
        {
            var existing = await db.BillingSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.WorkspaceId == DefaultWorkspaceId, ct);
            if (existing is not null) return;

            db.BillingSettings.Add(new BillingSettings
            {
                WorkspaceId = DefaultWorkspaceId,
                MarkupPercentage = 35m,
                Currency = "USD",
                RefundPolicy =
                    "Top-up balances are non-refundable once added. Failed messages are automatically " +
                    "refunded to your wallet. Manual refunds for billing errors can be requested via " +
                    "support — contact us within 30 days of the charge.",
                UpdatedAt = DateTime.UtcNow
            });

            try
            {
                await db.SaveChangesAsync(ct);
                logger.LogInformation("[SEED] Created BillingSettings for WorkspaceId={WorkspaceId} (markup 35%, USD).", DefaultWorkspaceId);
            }
            catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
            {
                logger.LogInformation("[SEED] BillingSettings already exists (concurrent seeder won the race) — continuing.");
            }
        }

        // ── MetaPricingRules (phase 1) ───────────────────────────────────

        private static async Task SeedPricingRulesAsync(
            AppDbContext db, ILogger logger, string? jsonPath, CancellationToken ct)
        {
            var anyExisting = await db.MetaPricingRules.AsNoTracking().AnyAsync(ct);
            if (anyExisting)
            {
                // Once seeded, the DB is authoritative. Admins edit rules through the UI (phase 10),
                // not by re-editing the JSON. Hands off.
                return;
            }

            var rules = LoadPricingRulesFromJson(jsonPath, logger);
            if (rules.Count == 0)
            {
                logger.LogWarning(
                    "[SEED] No pricing rules to seed. Phase-1 PricingService will return $0 quotes " +
                    "until rules are added — sends will succeed for free, which is wrong for production. " +
                    "Make sure {Path} exists and is well-formed.",
                    jsonPath ?? "Resources/billing/meta-pricing.json");
                return;
            }

            var now = DateTime.UtcNow;
            foreach (var raw in rules)
            {
                if (!Enum.TryParse<MetaMessageCategory>(raw.Category, ignoreCase: true, out var cat))
                {
                    logger.LogWarning("[SEED] Skipping pricing rule with unknown category {Cat}.", raw.Category);
                    continue;
                }

                var country = string.IsNullOrWhiteSpace(raw.CountryCode) ? "**" : raw.CountryCode!.Trim().ToUpperInvariant();
                if (country.Length != 2) country = "**";

                db.MetaPricingRules.Add(new MetaPricingRule
                {
                    Category = cat,
                    CountryCode = country,
                    BasePriceUsd = raw.BasePriceUsd,
                    EffectiveFrom = now,
                    EffectiveTo = null,
                    Source = raw.Source,
                    CreatedAt = now
                });
            }

            await db.SaveChangesAsync(ct);
            logger.LogInformation("[SEED] Seeded {Count} initial Meta pricing rules.", rules.Count);
        }

        private static List<RawPricingRule> LoadPricingRulesFromJson(string? jsonPath, ILogger logger)
        {
            if (string.IsNullOrWhiteSpace(jsonPath) || !File.Exists(jsonPath))
            {
                logger.LogWarning("[SEED] Pricing rules JSON not found at {Path}. Skipping rule seed.", jsonPath ?? "(unset)");
                return new List<RawPricingRule>();
            }

            try
            {
                using var stream = File.OpenRead(jsonPath);
                var doc = JsonSerializer.Deserialize<RawPricingRulesFile>(stream, JsonOptions);
                return doc?.Rules ?? new List<RawPricingRule>();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[SEED] Failed to parse pricing rules JSON at {Path}.", jsonPath);
                return new List<RawPricingRule>();
            }
        }

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        private sealed class RawPricingRulesFile
        {
            [JsonPropertyName("rules")]
            public List<RawPricingRule>? Rules { get; set; }
        }

        private sealed class RawPricingRule
        {
            [JsonPropertyName("category")]    public string? Category    { get; set; }
            [JsonPropertyName("countryCode")] public string? CountryCode { get; set; }
            [JsonPropertyName("basePriceUsd")] public decimal BasePriceUsd { get; set; }
            [JsonPropertyName("source")]      public string? Source      { get; set; }
        }

        private static bool IsUniqueConstraintViolation(DbUpdateException ex)
        {
            if (ex.InnerException is Microsoft.Data.SqlClient.SqlException sqlEx)
                return sqlEx.Number is 2601 or 2627;

            var msg = ex.InnerException?.Message ?? ex.Message;
            return msg.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("duplicate", StringComparison.OrdinalIgnoreCase);
        }
    }
}
