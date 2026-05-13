using ChatCRM.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;

namespace ChatCRM.Infrastructure.Services.Payments
{
    /// <summary>
    /// Stripe implementation of <see cref="IPaymentProvider"/>. Configures the Stripe SDK's
    /// global API key on first use; webhook signature verification is per-call so we can
    /// surface a clean 400 for tampered payloads. Never logs the secret key or webhook secret.
    /// </summary>
    public sealed class StripePaymentProvider : IPaymentProvider
    {
        private readonly StripeOptions _options;
        private readonly ILogger<StripePaymentProvider> _logger;
        private bool _configured;
        private readonly object _gate = new();

        public StripePaymentProvider(IOptions<StripeOptions> options, ILogger<StripePaymentProvider> logger)
        {
            _options = options.Value;
            _logger = logger;
        }

        public bool IsConfigured => _options.IsConfigured;

        public async Task<CheckoutSessionResult> CreateCheckoutSessionAsync(
            decimal amountUsd,
            string customerEmail,
            string? existingCustomerId,
            string successUrl,
            string cancelUrl,
            IDictionary<string, string> metadata,
            CancellationToken cancellationToken = default)
        {
            EnsureConfigured();

            var amountCents = (long)Math.Round(amountUsd * 100m, MidpointRounding.AwayFromZero);

            var options = new SessionCreateOptions
            {
                Mode = "payment",
                LineItems = new List<SessionLineItemOptions>
                {
                    new() {
                        Quantity = 1,
                        PriceData = new SessionLineItemPriceDataOptions
                        {
                            Currency = "usd",
                            UnitAmount = amountCents,
                            ProductData = new SessionLineItemPriceDataProductDataOptions
                            {
                                Name = "ChatCRM wallet top-up",
                                Description = $"Adds ${amountUsd:0.00} to your ChatCRM messaging balance."
                            }
                        }
                    }
                },
                SuccessUrl = successUrl,
                CancelUrl = cancelUrl,
                Metadata = metadata.ToDictionary(kv => kv.Key, kv => kv.Value)
            };

            // Reuse the saved Stripe Customer if we have one (lets the user see saved cards). On
            // the first top-up we pass CustomerEmail instead, and Stripe will create a new
            // customer behind the scenes — we capture the resulting customer id from the webhook
            // and persist it on the wallet for next time.
            if (!string.IsNullOrWhiteSpace(existingCustomerId))
                options.Customer = existingCustomerId;
            else if (!string.IsNullOrWhiteSpace(customerEmail))
                options.CustomerEmail = customerEmail;

            options.CustomerCreation = string.IsNullOrWhiteSpace(existingCustomerId) ? "always" : null;

            var service = new SessionService();
            var session = await service.CreateAsync(options, cancellationToken: cancellationToken);

            return new CheckoutSessionResult(session.Id, session.Url);
        }

        public WebhookEvent VerifyAndParseWebhook(string rawBody, string signatureHeader)
        {
            EnsureConfigured();

            // EventUtility.ConstructEvent verifies the signature using HMAC-SHA256. A failed
            // verification throws StripeException — the controller turns that into HTTP 400.
            //
            // throwOnApiVersionMismatch=false is required because Stripe CLI (and live Stripe)
            // ship newer API versions than the Stripe.NET package's pinned version. We only
            // touch a handful of fields (id, type, session/customer/intent ids, amount) — those
            // are stable across API versions. Strict matching would refuse every event from a
            // newer-than-pinned Stripe and force a NuGet bump on every Stripe API release.
            var stripeEvent = EventUtility.ConstructEvent(
                rawBody, signatureHeader, _options.WebhookSecret,
                throwOnApiVersionMismatch: false);

            string? sessionId = null;
            string? customerId = null;
            decimal? amountUsd = null;
            string? paymentIntentId = null;
            string? chargeId = null;
            IReadOnlyDictionary<string, string>? metadata = null;

            switch (stripeEvent.Data.Object)
            {
                case Session s:
                    sessionId = s.Id;
                    customerId = s.CustomerId;
                    amountUsd = s.AmountTotal.HasValue ? s.AmountTotal.Value / 100m : null;
                    paymentIntentId = s.PaymentIntentId;
                    metadata = s.Metadata as IReadOnlyDictionary<string, string>
                              ?? (s.Metadata is null ? null : new Dictionary<string, string>(s.Metadata));
                    break;
                case PaymentIntent pi:
                    paymentIntentId = pi.Id;
                    customerId = pi.CustomerId;
                    amountUsd = pi.Amount / 100m;
                    metadata = pi.Metadata is null ? null : new Dictionary<string, string>(pi.Metadata);
                    break;
                case Charge ch:
                    chargeId = ch.Id;
                    paymentIntentId = ch.PaymentIntentId;
                    customerId = ch.CustomerId;
                    amountUsd = ch.Amount / 100m;
                    metadata = ch.Metadata is null ? null : new Dictionary<string, string>(ch.Metadata);
                    break;
            }

            return new WebhookEvent(
                Id: stripeEvent.Id,
                Type: stripeEvent.Type,
                CheckoutSessionId: sessionId,
                CustomerId: customerId,
                AmountUsd: amountUsd,
                PaymentIntentId: paymentIntentId,
                ChargeId: chargeId,
                Metadata: metadata);
        }

        public async Task<bool> RefundChargeAsync(string chargeId, decimal? amountUsd, string reason, CancellationToken cancellationToken = default)
        {
            EnsureConfigured();

            try
            {
                var service = new RefundService();
                var options = new RefundCreateOptions
                {
                    Charge = chargeId,
                    Reason = "requested_by_customer",
                    Metadata = new Dictionary<string, string> { ["reason"] = reason }
                };
                if (amountUsd.HasValue)
                    options.Amount = (long)Math.Round(amountUsd.Value * 100m, MidpointRounding.AwayFromZero);

                var refund = await service.CreateAsync(options, cancellationToken: cancellationToken);
                _logger.LogInformation("[STRIPE] Refunded charge {Charge} (refund {Refund}); amount={Amount}.", chargeId, refund.Id, refund.Amount / 100m);
                return refund.Status == "succeeded" || refund.Status == "pending";
            }
            catch (StripeException ex)
            {
                _logger.LogError(ex, "[STRIPE] Refund failed for charge {Charge}.", chargeId);
                return false;
            }
        }

        public async Task<SetupIntentResult> CreateSetupIntentAsync(string customerId, CancellationToken cancellationToken = default)
        {
            EnsureConfigured();
            if (string.IsNullOrWhiteSpace(customerId))
                throw new InvalidOperationException("Customer id required to create a SetupIntent.");

            var service = new SetupIntentService();
            var options = new SetupIntentCreateOptions
            {
                Customer = customerId,
                PaymentMethodTypes = new List<string> { "card" },
                // off_session lets us charge the saved card later without the customer present.
                Usage = "off_session",
                AutomaticPaymentMethods = null
            };
            var si = await service.CreateAsync(options, cancellationToken: cancellationToken);
            _logger.LogInformation("[STRIPE] Created SetupIntent {Id} for customer {Customer}.", si.Id, customerId);
            return new SetupIntentResult(si.Id, si.ClientSecret);
        }

        public async Task<OffSessionChargeResult> ChargeSavedPaymentMethodAsync(
            string customerId, string paymentMethodId, decimal amountUsd,
            IDictionary<string, string> metadata, string idempotencyKey,
            CancellationToken cancellationToken = default)
        {
            EnsureConfigured();
            if (string.IsNullOrWhiteSpace(customerId))   throw new InvalidOperationException("Customer id required.");
            if (string.IsNullOrWhiteSpace(paymentMethodId)) throw new InvalidOperationException("Payment method id required.");
            if (amountUsd <= 0m) throw new InvalidOperationException("Amount must be positive.");

            var amountCents = (long)Math.Round(amountUsd * 100m, MidpointRounding.AwayFromZero);

            var service = new PaymentIntentService();
            var options = new PaymentIntentCreateOptions
            {
                Amount = amountCents,
                Currency = "usd",
                Customer = customerId,
                PaymentMethod = paymentMethodId,
                // off_session + confirm=true tells Stripe to charge immediately without 3DS prompt.
                // If the card requires authentication Stripe returns authentication_required and
                // the call throws — caller disables auto-recharge to avoid spamming.
                Confirm = true,
                OffSession = true,
                Metadata = metadata as Dictionary<string, string> ?? new Dictionary<string, string>(metadata)
            };

            var requestOptions = new RequestOptions { IdempotencyKey = idempotencyKey };
            var pi = await service.CreateAsync(options, requestOptions, cancellationToken);
            var chargeId = pi.LatestChargeId;
            _logger.LogInformation("[STRIPE] Off-session charge succeeded — pi={Pi}, charge={Charge}, amount={Amount}.",
                pi.Id, chargeId, amountUsd);
            return new OffSessionChargeResult(pi.Id, chargeId, amountUsd);
        }

        // ── helpers ──────────────────────────────────────────────────────────

        private void EnsureConfigured()
        {
            if (!IsConfigured)
                throw new InvalidOperationException(
                    "Stripe is not configured. Set Stripe:PublishableKey, Stripe:SecretKey, and " +
                    "Stripe:WebhookSecret in configuration before using payment endpoints.");

            // Idempotent global config — set once, on first real use. Locking is cheap and
            // avoids a tiny race in the unlikely first-call concurrent scenario.
            if (_configured) return;
            lock (_gate)
            {
                if (_configured) return;
                StripeConfiguration.ApiKey = _options.SecretKey;
                _configured = true;
            }
        }
    }
}
