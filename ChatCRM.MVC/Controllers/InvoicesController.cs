using ChatCRM.Application.Interfaces;
using ChatCRM.Domain.Entities;
using ChatCRM.Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace ChatCRM.MVC.Controllers
{
    /// <summary>
    /// Workspace invoice surface. List / generate / issue / download. v1 keeps it tied to
    /// <see cref="Permissions.BillingView"/> for read and <see cref="Permissions.BillingAdminRefund"/>
    /// for write — invoicing is admin-tier in scope.
    /// </summary>
    [Authorize]
    public sealed class InvoicesController : Controller
    {
        private readonly IInvoiceService _invoices;
        private readonly UserManager<User> _userManager;
        private readonly ILogger<InvoicesController> _logger;

        public InvoicesController(
            IInvoiceService invoices,
            UserManager<User> userManager,
            ILogger<InvoicesController> logger)
        {
            _invoices = invoices;
            _userManager = userManager;
            _logger = logger;
        }

        [HttpGet("/dashboard/billing/invoices")]
        [RequirePermission(Permissions.BillingView)]
        public IActionResult Index() => View();

        [HttpGet("/dashboard/billing/invoices/list")]
        [RequirePermission(Permissions.BillingView)]
        public async Task<IActionResult> List(CancellationToken ct)
        {
            var rows = await _invoices.ListAsync(ct);
            return Json(rows);
        }

        [HttpGet("/dashboard/billing/invoices/{id:int}")]
        [RequirePermission(Permissions.BillingView)]
        public async Task<IActionResult> Detail(int id, CancellationToken ct)
        {
            var detail = await _invoices.GetAsync(id, ct);
            return detail is null ? NotFound() : Json(detail);
        }

        public sealed class GenerateRequest { public int? Year { get; set; } public int? Month { get; set; } }

        [HttpPost("/dashboard/billing/invoices/generate")]
        [ValidateAntiForgeryToken]
        [RequirePermission(Permissions.BillingAdminRefund)]
        public async Task<IActionResult> Generate([FromBody] GenerateRequest? body, CancellationToken ct)
        {
            var userId = _userManager.GetUserId(User) ?? string.Empty;
            var nowUtc = DateTime.UtcNow;
            var year = body?.Year ?? nowUtc.Year;
            var month = body?.Month ?? nowUtc.Month;
            if (month < 1 || month > 12) return BadRequest(new { error = "Invalid month." });

            var anyDay = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
            try
            {
                var detail = await _invoices.GenerateForMonthAsync(anyDay, userId, ct);
                return Json(detail);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Invoice generate failed for {Year}-{Month}", year, month);
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpPost("/dashboard/billing/invoices/{id:int}/issue")]
        [ValidateAntiForgeryToken]
        [RequirePermission(Permissions.BillingAdminRefund)]
        public async Task<IActionResult> Issue(int id, CancellationToken ct)
        {
            var userId = _userManager.GetUserId(User) ?? string.Empty;
            try
            {
                var detail = await _invoices.IssueAsync(id, userId, ct);
                return Json(detail);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpGet("/dashboard/billing/invoices/{id:int}.pdf")]
        [RequirePermission(Permissions.BillingView)]
        public async Task<IActionResult> Pdf(int id, CancellationToken ct)
        {
            try
            {
                var bytes = await _invoices.RenderPdfAsync(id, ct);
                var filename = $"invoice-{id}.pdf";
                var detail = await _invoices.GetAsync(id, ct);
                if (detail is not null) filename = $"{detail.Number}.pdf";
                return File(bytes, "application/pdf", filename);
            }
            catch (InvalidOperationException ex)
            {
                return NotFound(new { error = ex.Message });
            }
        }
    }
}
