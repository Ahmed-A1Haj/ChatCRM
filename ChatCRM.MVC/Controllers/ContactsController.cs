using ChatCRM.Application.Chats.DTOs;
using ChatCRM.Application.Contacts.DTOs;
using ChatCRM.Application.Contacts.Import;
using ChatCRM.Application.Interfaces;
using ChatCRM.Domain.Entities;
using ChatCRM.Infrastructure.Authorization;
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
        private readonly IContactFileService _files;
        private readonly UserManager<User> _userManager;
        private readonly ILogger<ContactsController> _logger;

        // Per-file cap is enforced in the service (25 MB); the request cap allows a small batch.
        private const long MaxFilesRequestBytes = 60 * 1024 * 1024;

        // Upload safety net — Excel files past this size are almost always wrong-file mistakes
        // (.csv exported as .xls, a database dump, etc). Kestrel's default request limit is
        // 30MB anyway; this just gives a clean 400 instead of a stream-read failure.
        private const long MaxUploadBytes = 10 * 1024 * 1024;

        public ContactsController(
            IContactsService service,
            IChatService chatService,
            IContactImportService import,
            IContactFileService files,
            UserManager<User> userManager,
            ILogger<ContactsController> logger)
        {
            _service = service;
            _chatService = chatService;
            _import = import;
            _files = files;
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

        // ── Kanban pipeline board ───────────────────────────────────────

        [HttpGet("/dashboard/pipeline")]
        public async Task<IActionResult> Pipeline(CancellationToken cancellationToken)
        {
            var team = await _chatService.GetTeamMembersAsync(cancellationToken);
            ViewBag.TeamMembers = team;
            ViewBag.CurrentUserId = _userManager.GetUserId(User);
            return View();
        }

        [HttpGet("/api/contacts/board")]
        public async Task<IActionResult> Board([FromQuery] PipelineBoardQuery q, CancellationToken cancellationToken)
        {
            var board = await _service.GetBoardAsync(q, cancellationToken);
            return Json(board);
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

        [HttpGet("/api/contacts/{id:int}/details")]
        public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
        {
            var details = await _service.GetDetailsAsync(id, cancellationToken);
            return details is null ? NotFound() : Json(details);
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

        // ── Private contact files (internal-only) ───────────────────────
        // Every endpoint is permission-gated: ContactsView to read, ContactsEdit to mutate.
        // Files are stored in private storage (App_Data) and only ever streamed back here —
        // they are never given a public URL and never enter any outbound contact communication.

        [HttpGet("/api/contacts/{id:int}/files")]
        [RequirePermission(Permissions.ContactsView)]
        public async Task<IActionResult> ListFiles(int id, CancellationToken cancellationToken)
        {
            var files = await _files.ListAsync(id, cancellationToken);
            return Ok(files);
        }

        [HttpPost("/api/contacts/{id:int}/files")]
        [ValidateAntiForgeryToken]
        [RequirePermission(Permissions.ContactsEdit)]
        [RequestSizeLimit(MaxFilesRequestBytes)]
        public async Task<IActionResult> UploadFiles(int id, [FromForm] List<IFormFile> files, CancellationToken cancellationToken)
        {
            if (files is null || files.Count == 0)
                return BadRequest(new { error = "Select at least one file to upload." });

            var userId = _userManager.GetUserId(User);
            var uploaded = new List<ContactFileDto>();
            var errors = new List<object>();

            foreach (var file in files)
            {
                if (file is null || file.Length == 0) continue;
                try
                {
                    using var ms = new MemoryStream();
                    await file.CopyToAsync(ms, cancellationToken);
                    var dto = await _files.UploadAsync(id, ms.ToArray(), file.FileName, file.ContentType ?? "", userId, cancellationToken);
                    uploaded.Add(dto);
                }
                catch (KeyNotFoundException)
                {
                    return NotFound(new { error = "Contact not found." });
                }
                catch (InvalidOperationException ex)
                {
                    errors.Add(new { file = file.FileName, error = ex.Message });
                }
            }

            return Ok(new { uploaded, errors });
        }

        [HttpGet("/api/contacts/{id:int}/files/{fileId:int}/download")]
        [RequirePermission(Permissions.ContactsView)]
        public async Task<IActionResult> DownloadFile(int id, int fileId, [FromQuery] bool inline, CancellationToken cancellationToken)
        {
            var content = await _files.OpenAsync(id, fileId, cancellationToken);
            if (content is null) return NotFound();

            // Don't let browsers MIME-sniff our private files into something executable.
            Response.Headers["X-Content-Type-Options"] = "nosniff";

            if (inline)
            {
                // Inline preview (images / PDFs). Only allow-listed, non-scriptable types reach here.
                var cd = new System.Net.Mime.ContentDisposition { FileName = content.DownloadName, Inline = true };
                Response.Headers["Content-Disposition"] = cd.ToString();
                return File(content.Stream, content.ContentType);
            }

            // Default: force a download (attachment).
            return File(content.Stream, content.ContentType, content.DownloadName);
        }

        [HttpPost("/api/contacts/{id:int}/files/{fileId:int}/rename")]
        [ValidateAntiForgeryToken]
        [RequirePermission(Permissions.ContactsEdit)]
        public async Task<IActionResult> RenameFile(int id, int fileId, [FromBody] RenameFileBody body, CancellationToken cancellationToken)
        {
            if (body is null || string.IsNullOrWhiteSpace(body.Name))
                return BadRequest(new { error = "A new name is required." });

            var dto = await _files.RenameAsync(id, fileId, body.Name, cancellationToken);
            return dto is null ? NotFound() : Ok(dto);
        }

        [HttpDelete("/api/contacts/{id:int}/files/{fileId:int}")]
        [ValidateAntiForgeryToken]
        [RequirePermission(Permissions.ContactsEdit)]
        public async Task<IActionResult> DeleteFile(int id, int fileId, CancellationToken cancellationToken)
        {
            var ok = await _files.DeleteAsync(id, fileId, cancellationToken);
            return ok ? Ok(new { deleted = true }) : NotFound();
        }

        // ── Tiny request bodies ─────────────────────────────────────────

        public class RenameFileBody           { public string? Name { get; set; } }
        public class SetLifecycleStageBody    { public byte Stage { get; set; } }
        public class AssignContactBody        { public string? UserId { get; set; } }
        public class SetContactStatusBody     { public byte Status { get; set; } }
        public class SetBlockBody             { public bool Blocked { get; set; } }
    }
}
