using ChatCRM.Application.Billing.DTOs;
using ChatCRM.Application.Billing.Exceptions;
using ChatCRM.Application.Interfaces;
using ChatCRM.Domain.Entities;
using ChatCRM.Infrastructure.Authorization;
using ChatCRM.Infrastructure.Services.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace ChatCRM.MVC.Controllers
{
    /// <summary>
    /// Workspace-level billing surface. Phase 2 covers:
    ///   • GET /dashboard/billing — read-only overview (balance, recent activity, refund policy).
    ///   • POST /dashboard/billing/adjust — manual credit/debit (admin only via BillingAdminRefund).
    /// Top-up and Stripe webhook receiver land in phase 3.
    /// </summary>
    [Authorize]
    public sealed class BillingController : Controller
    {
        private const string TopUpRateLimitCacheKeyPrefix = "topup-rate:";

        private readonly IWalletService _walletService;
        private readonly IPaymentProvider _paymentProvider;
        private readonly StripeOptions _stripeOptions;
        private readonly IMemoryCache _cache;
        private readonly UserManager<User> _userManager;
        private readonly ILogger<BillingController> _logger;

        public BillingController(
            IWalletService walletService,
            IPaymentProvider paymentProvider,
            IOptions<StripeOptions> stripeOptions,
            IMemoryCache cache,
            UserManager<User> userManager,
            ILogger<BillingController> logger)
        {
            _walletService = walletService;
            _paymentProvider = paymentProvider;
            _stripeOptions = stripeOptions.Value;
            _cache = cache;
            _userManager = userManager;
            _logger = logger;
        }

        [HttpGet("/dashboard/billing")]
        [RequirePermission(Permissions.BillingView)]
        public async Task<IActionResult> Index(CancellationToken cancellationToken)
        {
            var overview = await _walletService.GetOverviewAsync(recentLimit: 10, cancellationToken);
            return View(overview);
        }

        [HttpPost("/dashboard/billing/adjust")]
        [ValidateAntiForgeryToken]
        [RequirePermission(Permissions.BillingAdminRefund)]
        public async Task<IActionResult> Adjust([FromForm] AdjustBalanceDto dto, CancellationToken cancellationToken)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            var displayName = string.IsNullOrWhiteSpace(user.FirstName)
                ? (user.Email ?? user.UserName ?? "Admin")
                : ($"{user.FirstName} {user.LastName}").Trim();

            try
            {
                var ledger = await _walletService.ApplyManualAdjustmentAsync(dto, user.Id, displayName, cancellationToken);
                TempData["StatusMessage"] = $"Wallet {(dto.IsCredit ? "credited" : "debited")} ${dto.AmountUsd:0.00} — new balance ${ledger.BalanceAfterUsd:0.00}.";
                TempData["StatusType"] = "success";
            }
            catch (InsufficientBalanceException ex)
            {
                TempData["StatusMessage"] = $"Cannot debit ${dto.AmountUsd:0.00} — current balance is ${ex.CurrentBalanceUsd:0.00}, hard floor is -$1.00.";
                TempData["StatusType"] = "danger";
            }
            catch (InvalidOperationException ex)
            {
                TempData["StatusMessage"] = ex.Message;
                TempData["StatusType"] = "danger";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected failure during manual wallet adjustment by user {UserId}.", user.Id);
                TempData["StatusMessage"] = "Adjustment failed — see server logs.";
                TempData["StatusType"] = "danger";
            }

            return RedirectToAction(nameof(Index));
        }

        // ── Top-up flow (phase 3) ───────────────────────────────────────────

        [HttpGet("/dashboard/billing/topup")]
        [RequirePermission(Permissions.BillingTopUp)]
        public IActionResult TopUp()
        {
            // The picker is read-only and just renders the preset amounts. POST does the
            // expensive work (rate-limit check, Stripe call, DB write).
            ViewBag.Presets = _stripeOptions.Presets is { Length: > 0 } p
                ? p
                : new[] { 25m, 50m, 100m, 250m, 500m };
            ViewBag.MinAmountUsd = _stripeOptions.MinAmountUsd;
            ViewBag.MaxAmountUsd = _stripeOptions.MaxAmountUsd;
            ViewBag.PaymentConfigured = _paymentProvider.IsConfigured;
            return View();
        }

        [HttpPost("/dashboard/billing/topup")]
        [ValidateAntiForgeryToken]
        [RequirePermission(Permissions.BillingTopUp)]
        public async Task<IActionResult> CreateTopUp([FromForm] CreateTopUpDto dto, CancellationToken cancellationToken)
        {
            if (!_paymentProvider.IsConfigured)
            {
                TempData["StatusMessage"] = "Payment processing is not configured. Contact support.";
                TempData["StatusType"] = "danger";
                return RedirectToAction(nameof(Index));
            }

            // Bounds check first — cheaper than the rate-limit lookup, gives clearer error.
            if (dto.AmountUsd < _stripeOptions.MinAmountUsd || dto.AmountUsd > _stripeOptions.MaxAmountUsd)
            {
                TempData["StatusMessage"] =
                    $"Top-up amount must be between ${_stripeOptions.MinAmountUsd:0.00} and ${_stripeOptions.MaxAmountUsd:0.00}.";
                TempData["StatusType"] = "danger";
                return RedirectToAction(nameof(TopUp));
            }

            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            // Sliding-hour rate limit per user. Cheap in-memory implementation; replace with
            // distributed cache if we ever scale out.
            if (!TryConsumeRateLimit(user.Id))
            {
                TempData["StatusMessage"] =
                    $"You've started too many top-ups in the past hour (limit {_stripeOptions.RateLimitSessionsPerHour}). Try again later.";
                TempData["StatusType"] = "warning";
                return RedirectToAction(nameof(TopUp));
            }

            // Build absolute success/cancel URLs that Stripe will redirect to.
            var origin = $"{Request.Scheme}://{Request.Host}";
            var successUrl = $"{origin}/dashboard/billing/topup/success?session_id={{CHECKOUT_SESSION_ID}}";
            var cancelUrl  = $"{origin}/dashboard/billing/topup/cancel";

            // Read existing Stripe Customer id off the wallet (if any) so returning customers
            // see their saved cards on the Checkout page. Lazy — null on first top-up.
            var stripeCustomerId = await _walletService.GetStripeCustomerIdAsync(cancellationToken);

            var metadata = new Dictionary<string, string>
            {
                ["userId"] = user.Id,
                ["walletWorkspaceId"] = "1"
            };

            CheckoutSessionResult session;
            try
            {
                session = await _paymentProvider.CreateCheckoutSessionAsync(
                    amountUsd: dto.AmountUsd,
                    customerEmail: user.Email ?? string.Empty,
                    existingCustomerId: stripeCustomerId,
                    successUrl: successUrl,
                    cancelUrl: cancelUrl,
                    metadata: metadata,
                    cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[TOPUP] Stripe session creation failed for user {UserId}.", user.Id);
                TempData["StatusMessage"] = "We couldn't reach the payment processor. Please try again in a moment.";
                TempData["StatusType"] = "danger";
                return RedirectToAction(nameof(TopUp));
            }

            // Insert the Pending ledger row with the session id as Reference. The webhook
            // will find this row and flip it to Succeeded once payment lands.
            await _walletService.RecordTopUpRequestAsync(dto.AmountUsd, session.SessionId, user.Id, cancellationToken);

            // 303 See Other — semantically correct for "I'm done with your POST, GO HERE".
            return Redirect(session.Url);
        }

        [HttpGet("/dashboard/billing/topup/success")]
        [RequirePermission(Permissions.BillingView)]
        public async Task<IActionResult> TopUpSuccess([FromQuery(Name = "session_id")] string? sessionId, CancellationToken cancellationToken)
        {
            // The webhook is the authoritative source of truth — by the time this success page
            // renders, the balance MIGHT not yet be credited (Stripe redirect can race the
            // webhook). The page tells the user "we're confirming your payment" and shows the
            // balance from the latest snapshot.
            var snap = await _walletService.GetSnapshotAsync(cancellationToken);
            ViewBag.SessionId = sessionId;
            ViewBag.Balance   = snap.BalanceUsd;
            return View();
        }

        [HttpGet("/dashboard/billing/topup/cancel")]
        [RequirePermission(Permissions.BillingView)]
        public IActionResult TopUpCancel() => View();

        // ── Auto-recharge (phase 11) ────────────────────────────────────────

        public sealed class SetupIntentRequest { } // placeholder for future fields

        [HttpPost("/dashboard/billing/setup-intent")]
        [ValidateAntiForgeryToken]
        [RequirePermission(Permissions.BillingTopUp)]
        public async Task<IActionResult> CreateSetupIntent(CancellationToken ct)
        {
            if (!_paymentProvider.IsConfigured)
                return BadRequest(new { error = "Payment processing is not configured." });

            var customerId = await _walletService.GetStripeCustomerIdAsync(ct);
            if (string.IsNullOrEmpty(customerId))
                return BadRequest(new { error = "Top up at least once to register a Stripe customer first." });

            var result = await _paymentProvider.CreateSetupIntentAsync(customerId, ct);
            return Json(new { clientSecret = result.ClientSecret, publishableKey = _stripeOptions.PublishableKey });
        }

        public sealed class ConfirmSetupRequest { public string PaymentMethodId { get; set; } = string.Empty; }

        [HttpPost("/dashboard/billing/setup-intent/confirm")]
        [ValidateAntiForgeryToken]
        [RequirePermission(Permissions.BillingTopUp)]
        public async Task<IActionResult> ConfirmSetupIntent([FromBody] ConfirmSetupRequest dto, CancellationToken ct)
        {
            if (dto is null || string.IsNullOrWhiteSpace(dto.PaymentMethodId))
                return BadRequest(new { error = "PaymentMethod id required." });

            await _walletService.SetDefaultPaymentMethodAsync(dto.PaymentMethodId, ct);
            return Ok(new { saved = true });
        }

        public sealed class AutoRechargeUpdateDto
        {
            public bool Enabled { get; set; }
            public decimal? TriggerUsd { get; set; }
            public decimal? AmountUsd { get; set; }
        }

        [HttpGet("/dashboard/billing/autorecharge")]
        [RequirePermission(Permissions.BillingView)]
        public async Task<IActionResult> AutoRechargeState(CancellationToken ct)
        {
            var w = await _walletService.GetSnapshotAsync(ct);
            var customer = await _walletService.GetStripeCustomerIdAsync(ct);
            // Pull additional fields from the DB directly via a fresh service call.
            var details = await HttpContext.RequestServices
                .GetRequiredService<ChatCRM.Persistence.AppDbContext>().Wallets
                .AsNoTracking()
                .Where(x => x.WorkspaceId == 1)
                .Select(x => new
                {
                    x.AutoRechargeEnabled,
                    x.AutoRechargeTriggerUsd,
                    x.AutoRechargeAmountUsd,
                    x.DefaultPaymentMethodId,
                    x.StripeCustomerId
                })
                .FirstAsync(ct);
            return Json(new
            {
                enabled = details.AutoRechargeEnabled,
                triggerUsd = details.AutoRechargeTriggerUsd,
                amountUsd = details.AutoRechargeAmountUsd,
                hasPaymentMethod = !string.IsNullOrEmpty(details.DefaultPaymentMethodId),
                hasStripeCustomer = !string.IsNullOrEmpty(details.StripeCustomerId)
            });
        }

        [HttpPost("/dashboard/billing/autorecharge")]
        [ValidateAntiForgeryToken]
        [RequirePermission(Permissions.BillingTopUp)]
        public async Task<IActionResult> UpdateAutoRecharge([FromBody] AutoRechargeUpdateDto dto, CancellationToken ct)
        {
            if (dto is null) return BadRequest(new { error = "Body required." });
            var userId = _userManager.GetUserId(User) ?? string.Empty;
            try
            {
                await _walletService.UpdateAutoRechargeAsync(dto.Enabled, dto.TriggerUsd, dto.AmountUsd, userId, ct);
                return Ok(new { saved = true });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        // ── Analytics (phase 8) ─────────────────────────────────────────────

        [HttpGet("/dashboard/billing/analytics")]
        [RequirePermission(Permissions.BillingView)]
        public IActionResult Analytics() => View();

        [HttpGet("/dashboard/billing/analytics/data")]
        [RequirePermission(Permissions.BillingView)]
        public async Task<IActionResult> AnalyticsData([FromQuery] string? range, CancellationToken ct)
        {
            var (from, to) = ResolveRange(range);
            var data = await _walletService.GetAnalyticsAsync(from, to, ct);
            return Json(data);
        }

        public sealed class TransactionsLogQueryDto
        {
            public string? Range { get; set; }
            public byte? Kind { get; set; }
            public byte? Status { get; set; }
            public int Page { get; set; } = 0;
            public int PageSize { get; set; } = 50;
        }

        [HttpGet("/dashboard/billing/transactions")]
        [RequirePermission(Permissions.BillingView)]
        public async Task<IActionResult> Transactions([FromQuery] TransactionsLogQueryDto query, CancellationToken ct)
        {
            var (from, to) = ResolveRange(query.Range);
            var page = await _walletService.QueryTransactionsAsync(new WalletTransactionLogQuery
            {
                FromUtc = from,
                ToUtc = to,
                Kind = query.Kind.HasValue ? (WalletTransactionKind)query.Kind.Value : null,
                Status = query.Status.HasValue ? (WalletTransactionStatus)query.Status.Value : null,
                Page = query.Page,
                PageSize = query.PageSize
            }, ct);
            return Json(page);
        }

        [HttpGet("/dashboard/billing/transactions.csv")]
        [RequirePermission(Permissions.BillingView)]
        public async Task<IActionResult> TransactionsCsv([FromQuery] TransactionsLogQueryDto query, CancellationToken ct)
        {
            // CSV export pulls up to 10000 rows — large enough for monthly statements without
            // streaming. Beyond that, transactional admins should use the API directly.
            var (from, to) = ResolveRange(query.Range);
            var page = await _walletService.QueryTransactionsAsync(new WalletTransactionLogQuery
            {
                FromUtc = from,
                ToUtc = to,
                Kind = query.Kind.HasValue ? (WalletTransactionKind)query.Kind.Value : null,
                Status = query.Status.HasValue ? (WalletTransactionStatus)query.Status.Value : null,
                Page = 0,
                PageSize = 100
            }, ct);

            // Walk additional pages until we have everything (or hit a hard cap).
            var rows = new List<WalletTransactionDto>(page.Rows);
            while (page.HasMore && rows.Count < 10_000)
            {
                page = await _walletService.QueryTransactionsAsync(new WalletTransactionLogQuery
                {
                    FromUtc = from,
                    ToUtc = to,
                    Kind = query.Kind.HasValue ? (WalletTransactionKind)query.Kind.Value : null,
                    Status = query.Status.HasValue ? (WalletTransactionStatus)query.Status.Value : null,
                    Page = page.Page + 1,
                    PageSize = 100
                }, ct);
                rows.AddRange(page.Rows);
            }

            var csv = new System.Text.StringBuilder();
            csv.AppendLine("CreatedAtUtc,Kind,Status,AmountUsd,BalanceAfterUsd,Reference,Reason,InitiatedBy");
            foreach (var r in rows)
            {
                csv.Append(r.CreatedAt.ToString("o")).Append(',');
                csv.Append(r.Kind).Append(',');
                csv.Append(r.Status).Append(',');
                csv.Append(r.AmountUsd.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(',');
                csv.Append(r.BalanceAfterUsd.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(',');
                csv.Append(EscapeCsv(r.Reference)).Append(',');
                csv.Append(EscapeCsv(r.Reason)).Append(',');
                csv.AppendLine(EscapeCsv(r.InitiatedByName));
            }

            var bytes = System.Text.Encoding.UTF8.GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
            return File(bytes, "text/csv", $"transactions-{DateTime.UtcNow:yyyy-MM-dd}.csv");
        }

        private static string EscapeCsv(string? value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            var needsQuotes = value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r');
            var escaped = value.Replace("\"", "\"\"");
            return needsQuotes ? $"\"{escaped}\"" : escaped;
        }

        private static (DateTime From, DateTime To) ResolveRange(string? range)
        {
            var nowUtc = DateTime.UtcNow;
            return (range ?? "mtd").ToLowerInvariant() switch
            {
                "7d"   => (nowUtc.Date.AddDays(-7), nowUtc.Date.AddDays(1)),
                "30d"  => (nowUtc.Date.AddDays(-30), nowUtc.Date.AddDays(1)),
                "90d"  => (nowUtc.Date.AddDays(-90), nowUtc.Date.AddDays(1)),
                "ytd"  => (new DateTime(nowUtc.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc), nowUtc.Date.AddDays(1)),
                _      => (new DateTime(nowUtc.Year, nowUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc), nowUtc.Date.AddDays(1))
            };
        }

        // ── helpers ─────────────────────────────────────────────────────────

        private bool TryConsumeRateLimit(string userId)
        {
            var key = TopUpRateLimitCacheKeyPrefix + userId;
            var entries = _cache.GetOrCreate(key, e =>
            {
                e.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
                return new List<DateTime>();
            })!;

            var now = DateTime.UtcNow;
            // Drop anything older than an hour (sliding window).
            entries.RemoveAll(t => t < now.AddHours(-1));

            if (entries.Count >= _stripeOptions.RateLimitSessionsPerHour) return false;
            entries.Add(now);
            // Re-set so eviction respects the freshest entry.
            _cache.Set(key, entries, TimeSpan.FromHours(1));
            return true;
        }

    }
}
