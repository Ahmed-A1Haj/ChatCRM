using ChatCRM.Application.Chats.DTOs;
using ChatCRM.Application.Contacts.DTOs;
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
        private readonly UserManager<User> _userManager;
        private readonly ILogger<ContactsController> _logger;

        public ContactsController(IContactsService service, IChatService chatService, UserManager<User> userManager, ILogger<ContactsController> logger)
        {
            _service = service;
            _chatService = chatService;
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

        // ── Tiny request bodies ─────────────────────────────────────────

        public class SetLifecycleStageBody    { public byte Stage { get; set; } }
        public class AssignContactBody        { public string? UserId { get; set; } }
        public class SetContactStatusBody     { public byte Status { get; set; } }
        public class SetBlockBody             { public bool Blocked { get; set; } }
    }
}
