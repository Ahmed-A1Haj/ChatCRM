using ChatCRM.Application.Chats.DTOs;
using ChatCRM.Application.Contacts.DTOs;
using ChatCRM.Application.Contacts.Import;
using ChatCRM.Application.Interfaces;
using ChatCRM.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace ChatCRM.MVC.Controllers
{
    [Authorize]
    public class ContactsController : Controller
    {
        private readonly IContactsService _service;
        private readonly IChatService _chatService;
        private readonly IContactImportService _import;
        private readonly UserManager<User> _userManager;
        private readonly ILogger<ContactsController> _logger;

        // Upload safety net — Excel files past this size are almost always wrong-file mistakes
        // (.csv exported as .xls, a database dump, etc). Kestrel's default request limit is
        // 30MB anyway; this just gives a clean 400 instead of a stream-read failure.
        private const long MaxUploadBytes = 10 * 1024 * 1024;

        public ContactsController(
            IContactsService service,
            IChatService chatService,
            IContactImportService import,
            UserManager<User> userManager,
            ILogger<ContactsController> logger)
        {
            _service = service;
            _chatService = chatService;
            _import = import;
            _userManager = userManager;
            _logger = logger;
        }

        // ── Page ────────────────────────────────────────────────────────

        [HttpGet("/dashboard/contacts")]
        public async Task<IActionResult> Index(CancellationToken cancellationToken)
        {
            var team = await _chatService.GetTeamMembersAsync(cancellationToken);
            ViewBag.TeamMembers = team;
            ViewBag.CurrentUserId = _userManager.GetUserId(User);
            return View();
        }

        // ── REST ────────────────────────────────────────────────────────

        [HttpGet("/api/contacts")]
        public async Task<IActionResult> List([FromQuery] ContactsListQuery q, CancellationToken cancellationToken)
        {
            var result = await _service.ListAsync(q, cancellationToken);
            return Ok(result);
        }

        [HttpPost("/api/contacts/{id:int}/lifecycle")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetLifecycle(int id, [FromBody] SetLifecycleStageBody body, CancellationToken cancellationToken)
        {
            try
            {
                await _service.SetLifecycleAsync(id, body.Stage, cancellationToken);
                return Ok(new { stage = body.Stage });
            }
            catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
        }

        [HttpPost("/api/contacts/{id:int}/assign")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Assign(int id, [FromBody] AssignContactBody body, CancellationToken cancellationToken)
        {
            try
            {
                await _service.SetAssigneeAsync(id, body.UserId, cancellationToken);
                return Ok(new { assigned = true });
            }
            catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
        }

        [HttpPost("/api/contacts/{id:int}/status")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetStatus(int id, [FromBody] SetContactStatusBody body, CancellationToken cancellationToken)
        {
            try
            {
                await _service.SetStatusAsync(id, body.Status, cancellationToken);
                return Ok(new { status = body.Status });
            }
            catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
        }

        [HttpPost("/api/contacts/{id:int}/language")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetLanguage(int id, [FromBody] UpdateContactLanguageDto body, CancellationToken cancellationToken)
        {
            try
            {
                await _service.SetLanguageAsync(id, body.Language, cancellationToken);
                return Ok(new { language = body.Language });
            }
            catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
        }

        [HttpPost("/api/contacts/{id:int}/block")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Block(int id, [FromBody] SetBlockBody body, CancellationToken cancellationToken)
        {
            try
            {
                await _service.SetBlockAsync(id, body.Blocked, cancellationToken);
                return Ok(new { blocked = body.Blocked });
            }
            catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
        }

        [HttpDelete("/api/contacts/{id:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
        {
            try
            {
                await _service.DeleteAsync(id, cancellationToken);
                return Ok(new { deleted = true });
            }
            catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
        }

        [HttpGet("/api/contacts/export")]
        public async Task<IActionResult> Export([FromQuery] ContactsListQuery q, CancellationToken cancellationToken)
        {
            var bytes = await _service.ExportCsvAsync(q, cancellationToken);
            var fileName = $"contacts-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv";
            return File(bytes, "text/csv", fileName);
        }

        // ── Import ──────────────────────────────────────────────────────

        private const string XlsxMime = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

        [HttpGet("/api/contacts/import/template")]
        public async Task<IActionResult> ImportTemplate(CancellationToken cancellationToken)
        {
            var bytes = await _import.GenerateTemplateAsync(cancellationToken);
            return File(bytes, XlsxMime, "contacts-import-template.xlsx");
        }

        [HttpPost("/api/contacts/import/preview")]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(MaxUploadBytes)]
        public async Task<IActionResult> ImportPreview([FromForm] IFormFile? file, CancellationToken cancellationToken)
        {
            if (file is null || file.Length == 0)
                return BadRequest(new { error = "No file uploaded." });
            if (file.Length > MaxUploadBytes)
                return BadRequest(new { error = "File is too large (max 10 MB)." });

            // Accept only Excel files — CSV imports would need a different parser and the
            // template we hand out is .xlsx.
            var ext = Path.GetExtension(file.FileName)?.ToLowerInvariant();
            if (ext is not ".xlsx" and not ".xls")
                return BadRequest(new { error = "Only .xlsx and .xls files are supported." });

            await using var stream = file.OpenReadStream();
            try
            {
                var preview = await _import.PreviewAsync(stream, file.FileName, cancellationToken);
                return Ok(preview);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Contact import preview failed for file '{File}'", file.FileName);
                return BadRequest(new { error = "Failed to process the uploaded file." });
            }
        }

        [HttpPost("/api/contacts/import/confirm")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ImportConfirm([FromBody] ConfirmImportDto body, CancellationToken cancellationToken)
        {
            if (body is null || body.Rows is null || body.Rows.Count == 0)
                return BadRequest(new { error = "No rows to import." });

            try
            {
                var result = await _import.ConfirmAsync(body, cancellationToken);
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpPost("/api/contacts/import/error-report")]
        [ValidateAntiForgeryToken]
        public IActionResult ImportErrorReport([FromBody] ErrorReportRequestDto body)
        {
            if (body is null || body.Errors is null || body.Errors.Count == 0)
                return BadRequest(new { error = "No errors to report." });

            var bytes = _import.BuildErrorReport(body);
            var fileName = $"contact-import-errors-{DateTime.UtcNow:yyyyMMdd-HHmmss}.xlsx";
            return File(bytes, XlsxMime, fileName);
        }

        // ── Tiny request bodies ─────────────────────────────────────────

        public class SetLifecycleStageBody    { public byte Stage { get; set; } }
        public class AssignContactBody        { public string? UserId { get; set; } }
        public class SetContactStatusBody     { public byte Status { get; set; } }
        public class SetBlockBody             { public bool Blocked { get; set; } }
    }
}
