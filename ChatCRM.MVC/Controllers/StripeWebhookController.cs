using ChatCRM.Application.Interfaces;
using ChatCRM.Domain.Entities;
using ChatCRM.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stripe;
using System.Text;

namespace ChatCRM.MVC.Controllers
{
    /// <summary>
    /// Receives Stripe webhook events. Mounted under <c>/api/webhooks/stripe</c> and exempted
    /// from auth + HTTPS redirect (Stripe POSTs from outside the cookie auth perimeter).
    ///
    /// Hardening:
    ///   • Signature verification is mandatory — rejects with 400 on tamper.
    ///   • Idempotency via <see cref="ProcessedStripeEvent"/> table — re-deliveries become no-ops.
    ///   • Always returns 200 (or 4xx for malformed) — never throws past the controller boundary,
    ///     because Stripe interprets 5xx as "retry me", which would amplify whatever failure we
    ///     were already trying to handle.
    /// </summary>
    [ApiController]
    [AllowAnonymous]
    [Route("api/webhooks/stripe")]
    public sealed class StripeWebhookController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly IPaymentProvider _paymentProvider;
        private readonly IWalletService _walletService;
        private readonly IBillingEmailSender _email;
        private readonly UserManager<User> _userManager;
        private readonly ILogger<StripeWebhookController> _logger;

        public StripeWebhookController(
            AppDbContext db,
            IPaymentProvider paymentProvider,
            IWalletService walletService,
            IBillingEmailSender email,
            UserManager<User> userManager,
            ILogger<StripeWebhookController> logger)
        {
            _db = db;
            _paymentProvider = paymentProvider;
            _walletService = walletService;
            _email = email;
            _userManager = userManager;
            _logger = logger;
        }

        [HttpPost]
        public async Task<IActionResult> Receive(CancellationToken cancellationToken)
        {
            // Read the raw body — we MUST verify the signature against the original bytes,
            // not a re-serialized model.
            string body;
            using (var reader = new StreamReader(Request.Body, Encoding.UTF8, leaveOpen: false))
                body = await reader.ReadToEndAsync(cancellationToken);

            var signature = Request.Headers["Stripe-Signature"].ToString();

            if (string.IsNullOrEmpty(signature))
            {
                _logger.LogWarning("[STRIPE-HOOK] Missing Stripe-Signature header.");
                return BadRequest();
            }

            WebhookEvent ev;
            try
            {
                ev = _paymentProvider.VerifyAndParseWebhook(body, signature);
            }
            catch (StripeException ex)
            {
                _logger.LogWarning(ex, "[STRIPE-HOOK] Signature verification failed.");
                return BadRequest();
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "[STRIPE-HOOK] Provider not configured.");
                return BadRequest();
            }

            // Idempotency check — Stripe retries up to 3 days on non-2xx, and occasionally
            // double-delivers on 2xx. The unique PK on EventId blocks the second insert.
            if (await _db.ProcessedStripeEvents.AnyAsync(e => e.EventId == ev.Id, cancellationToken))
            {
                _logger.LogInformation("[STRIPE-HOOK] Event {EventId} already processed — short-circuiting.", ev.Id);
                return Ok();
            }

            try
            {
                await DispatchAsync(ev, cancellationToken);
            }
            catch (Exception ex)
            {
                // Don't 5xx — Stripe will retry forever. Log + 200 so we move on; ops can
                // replay manually from the Stripe dashboard if a real bug needs re-processing.
                _logger.LogError(ex, "[STRIPE-HOOK] Handler threw for event {EventId} ({Type}). Marking processed and moving on.", ev.Id, ev.Type);
            }

            try
            {
                _db.ProcessedStripeEvents.Add(new ProcessedStripeEvent
                {
                    EventId = ev.Id,
                    EventType = ev.Type,
                    ProcessedAtUtc = DateTime.UtcNow
                });
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException) { /* duplicate insert under concurrency — fine. */ }

            return Ok();
        }

        // ── per-event-type handlers ──────────────────────────────────────────

        private async Task DispatchAsync(WebhookEvent ev, CancellationToken ct)
        {
            switch (ev.Type)
            {
                case "checkout.session.completed":
                    await OnCheckoutCompletedAsync(ev, ct);
                    break;

                case "checkout.session.expired":
                case "checkout.session.async_payment_failed":
                case "payment_intent.payment_failed":
                    await OnPaymentFailedAsync(ev, ct);
                    break;

                case "charge.refunded":
                    // Phase 4 will use this for message-level refunds. v1 just logs.
                    _logger.LogInformation("[STRIPE-HOOK] charge.refunded received (charge {Charge}); no-op until phase 4.", ev.ChargeId);
                    break;

                default:
                    _logger.LogDebug("[STRIPE-HOOK] Ignoring event type {Type}.", ev.Type);
                    break;
            }
        }

        private async Task OnCheckoutCompletedAsync(WebhookEvent ev, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(ev.CheckoutSessionId))
            {
                _logger.LogWarning("[STRIPE-HOOK] checkout.session.completed without session id.");
                return;
            }

            var tx = await _walletService.CompleteTopUpAsync(ev.CheckoutSessionId, ev.CustomerId, ev.Id, ct);
            if (tx is null || tx.Status != WalletTransactionStatus.Succeeded) return;

            // Best-effort receipt email. Failures are swallowed inside the email sender so
            // a flaky SMTP can't keep us replying non-2xx to Stripe.
            if (!string.IsNullOrWhiteSpace(tx.InitiatedByUserId))
            {
                var user = await _userManager.FindByIdAsync(tx.InitiatedByUserId);
                if (user is not null && !string.IsNullOrWhiteSpace(user.Email))
                {
                    var displayName = string.IsNullOrWhiteSpace(user.FirstName)
                        ? (user.Email ?? "")
                        : $"{user.FirstName} {user.LastName}".Trim();
                    await _email.SendTopUpReceiptAsync(user.Email, displayName, tx.AmountUsd, tx.BalanceAfterUsd, ev.CheckoutSessionId, ct);
                }
            }
        }

        private async Task OnPaymentFailedAsync(WebhookEvent ev, CancellationToken ct)
        {
            var sessionId = ev.CheckoutSessionId;
            if (string.IsNullOrEmpty(sessionId))
            {
                // Some payment_intent events don't carry a session id directly. Look up the
                // pending row by payment-intent if needed in a future enhancement; for now
                // we log + skip rather than guess.
                _logger.LogInformation("[STRIPE-HOOK] {Type} without checkout session id — no top-up row to mark Failed.", ev.Type);
                return;
            }

            await _walletService.MarkTopUpFailedAsync(sessionId, $"Stripe {ev.Type}", ct);
        }
    }
}
