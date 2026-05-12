namespace ChatCRM.Infrastructure.Services.Payments
{
    /// <summary>
    /// Stripe configuration. Bound from <c>Stripe</c> section of appsettings.
    /// All keys are sensitive — populate in environment variables / appsettings.Development.json,
    /// never in committed appsettings.json.
    ///
    /// Test-mode keys start with <c>pk_test_</c> / <c>sk_test_</c>; live with <c>pk_live_</c> / <c>sk_live_</c>.
    /// The same code path serves both — Stripe routes by key prefix.
    /// </summary>
    public sealed class StripeOptions
    {
        /// <summary>Used by client-side Stripe.js. Safe to expose. Empty disables top-up.</summary>
        public string PublishableKey { get; set; } = string.Empty;

        /// <summary>Used by server-side calls. NEVER log or expose. Empty disables top-up.</summary>
        public string SecretKey { get; set; } = string.Empty;

        /// <summary>Webhook signing secret (<c>whsec_…</c>). Required to verify incoming events.</summary>
        public string WebhookSecret { get; set; } = string.Empty;

        /// <summary>Min top-up per session (USD). Below this, Stripe fees eat the margin.</summary>
        public decimal MinAmountUsd { get; set; } = 10m;

        /// <summary>Max top-up per session (USD). Above this, fraud + dispute exposure rises.</summary>
        public decimal MaxAmountUsd { get; set; } = 1000m;

        /// <summary>
        /// Preset amounts shown on the picker. Default is empty so .NET config binding can
        /// REPLACE — not append to — the JSON values; appsettings.json is the source of truth.
        /// Controller falls back to a hard-coded list if the config is missing entirely.
        /// </summary>
        public decimal[] Presets { get; set; } = Array.Empty<decimal>();

        /// <summary>Per-user rate limit on Checkout-session creation. 5 per sliding-hour.</summary>
        public int RateLimitSessionsPerHour { get; set; } = 5;

        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(SecretKey) &&
            !string.IsNullOrWhiteSpace(PublishableKey) &&
            !string.IsNullOrWhiteSpace(WebhookSecret);
    }
}
