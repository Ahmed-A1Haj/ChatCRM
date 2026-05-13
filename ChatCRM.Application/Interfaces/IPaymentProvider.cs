namespace ChatCRM.Application.Interfaces
{
    /// <summary>
    /// Abstraction over an external payment processor. v1 only ships Stripe. Paymob / Fawry can
    /// drop in later by implementing this interface and registering as a named provider.
    /// </summary>
    public interface IPaymentProvider
    {
        /// <summary>True when API keys + webhook secret are present.</summary>
        bool IsConfigured { get; }

        /// <summary>
        /// Create a hosted checkout session for a one-time top-up.
        /// </summary>
        /// <param name="amountUsd">Top-up amount in USD. Caller is responsible for min/max enforcement.</param>
        /// <param name="customerEmail">Pre-fills the email on the Checkout page.</param>
        /// <param name="existingCustomerId">If the wallet already has a Stripe Customer attached, pass it. Null on first top-up.</param>
        /// <param name="successUrl">Absolute URL Stripe redirects to on success. Should include a placeholder for session_id.</param>
        /// <param name="cancelUrl">Absolute URL Stripe redirects to on cancel.</param>
        /// <param name="metadata">Free-form key/value pairs that ride with the session — used to correlate the webhook back to our WalletTransaction.</param>
        Task<CheckoutSessionResult> CreateCheckoutSessionAsync(
            decimal amountUsd,
            string customerEmail,
            string? existingCustomerId,
            string successUrl,
            string cancelUrl,
            IDictionary<string, string> metadata,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Verify the signature on a webhook payload and return the decoded event. Throws on
        /// signature failure — the controller turns that into a 400.
        /// </summary>
        WebhookEvent VerifyAndParseWebhook(string rawBody, string signatureHeader);

        /// <summary>
        /// Refund a charge. Used by phase 4 to refund failed/rejected outbound messages.
        /// Pass null for amount to refund the full charge.
        /// </summary>
        Task<bool> RefundChargeAsync(string chargeId, decimal? amountUsd, string reason, CancellationToken cancellationToken = default);

        /// <summary>
        /// Create a SetupIntent so the customer can save a payment method for future off-session
        /// charges (auto-recharge). Returns a client secret the front-end uses with Stripe.js to
        /// confirm the SetupIntent. The default payment method id lands on the wallet via the
        /// <c>setup_intent.succeeded</c> webhook.
        /// </summary>
        Task<SetupIntentResult> CreateSetupIntentAsync(
            string customerId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Charge a saved payment method off-session for the auto-recharge flow. Returns the
        /// resulting charge id on success. Failures (insufficient funds, card declined, etc.)
        /// throw with the Stripe error message — caller handles by disabling auto-recharge.
        /// </summary>
        Task<OffSessionChargeResult> ChargeSavedPaymentMethodAsync(
            string customerId, string paymentMethodId, decimal amountUsd,
            IDictionary<string, string> metadata, string idempotencyKey,
            CancellationToken cancellationToken = default);
    }

    /// <summary>Outcome of CreateCheckoutSessionAsync — give the caller the URL to redirect to + the session id for our records.</summary>
    public sealed record CheckoutSessionResult(string SessionId, string Url);

    /// <summary>Result of <see cref="IPaymentProvider.CreateSetupIntentAsync"/> — the client secret is what Stripe.js needs.</summary>
    public sealed record SetupIntentResult(string SetupIntentId, string ClientSecret);

    /// <summary>Result of an off-session charge (auto-recharge).</summary>
    public sealed record OffSessionChargeResult(string PaymentIntentId, string? ChargeId, decimal AmountUsd);

    /// <summary>Decoded webhook event. Provider-agnostic shape — concrete fields vary by Type.</summary>
    public sealed record WebhookEvent(
        string Id,
        string Type,
        string? CheckoutSessionId,
        string? CustomerId,
        decimal? AmountUsd,
        string? PaymentIntentId,
        string? ChargeId,
        IReadOnlyDictionary<string, string>? Metadata);
}
