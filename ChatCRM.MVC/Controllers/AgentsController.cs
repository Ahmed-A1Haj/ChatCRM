using ChatCRM.Application.Agents.DTOs;
using ChatCRM.Application.Agents.Exceptions;
using ChatCRM.Application.Interfaces;
using ChatCRM.Domain.Entities;
using ChatCRM.Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace ChatCRM.MVC.Controllers
{
    /// <summary>
    /// AI Agents management. View is gated by <see cref="Permissions.AgentsView"/>;
    /// mutations require <see cref="Permissions.AgentsManage"/>. Avatars upload to
    /// <c>/uploads/agents/{id}/...</c> with strict mime + size checks.
    /// </summary>
    [Authorize]
    [Route("/dashboard/agents")]
    public sealed class AgentsController : Controller
    {
        private static readonly Dictionary<string, string> AllowedAvatarTypes =
            new(StringComparer.OrdinalIgnoreCase)
            {
                [".jpg"]  = "image/jpeg",
                [".jpeg"] = "image/jpeg",
                [".png"]  = "image/png",
                [".webp"] = "image/webp"
            };
        private const long MaxAvatarBytes = 2 * 1024 * 1024;

        private readonly IAgentService _agents;
        private readonly UserManager<User> _userManager;
        private readonly IWebHostEnvironment _env;
        private readonly IStringLocalizer _localizer;
        private readonly ILogger<AgentsController> _logger;

        public AgentsController(
            IAgentService agents,
            UserManager<User> userManager,
            IWebHostEnvironment env,
            IStringLocalizer localizer,
            ILogger<AgentsController> logger)
        {
            _agents = agents;
            _userManager = userManager;
            _env = env;
            _localizer = localizer;
            _logger = logger;
        }

        [HttpGet("")]
        [RequirePermission(Permissions.AgentsView)]
        public IActionResult Index() => View();

        [HttpGet("list")]
        [RequirePermission(Permissions.AgentsView)]
        public async Task<IActionResult> List([FromQuery] string? filter, [FromQuery] string? q, CancellationToken ct)
        {
            var f = (filter ?? "all").ToLowerInvariant() switch
            {
                "active"   => AgentListFilter.Active,
                "inactive" => AgentListFilter.Inactive,
                _          => AgentListFilter.All
            };
            var rows = await _agents.ListAsync(f, q, ct);
            return Json(rows);
        }

        [HttpGet("{id:int}")]
        [RequirePermission(Permissions.AgentsView)]
        public async Task<IActionResult> Detail(int id, CancellationToken ct)
        {
            var detail = await _agents.GetAsync(id, ct);
            return detail is null ? NotFound() : Json(detail);
        }

        [HttpPost("")]
        [ValidateAntiForgeryToken]
        [RequirePermission(Permissions.AgentsManage)]
        public async Task<IActionResult> Create([FromBody] AgentUpsertDto dto, CancellationToken ct)
        {
            var userId = _userManager.GetUserId(User) ?? string.Empty;
            try
            {
                var detail = await _agents.CreateAsync(dto, userId, ct);
                return Json(detail);
            }
            catch (AgentValidationException ex)
            {
                return UnprocessableEntity(BuildValidationPayload(ex));
            }
            catch (AgentInvariantException ex)
            {
                return Conflict(new { error = ex.Message, code = ex.Code });
            }
        }

        [HttpPost("{id:int}")]
        [ValidateAntiForgeryToken]
        [RequirePermission(Permissions.AgentsManage)]
        public async Task<IActionResult> Update(int id, [FromBody] AgentUpsertDto dto, CancellationToken ct)
        {
            var userId = _userManager.GetUserId(User) ?? string.Empty;
            try
            {
                var detail = await _agents.UpdateAsync(id, dto, userId, ct);
                return Json(detail);
            }
            catch (AgentValidationException ex)
            {
                return UnprocessableEntity(BuildValidationPayload(ex));
            }
            catch (AgentInvariantException ex)
            {
                return Conflict(new { error = ex.Message, code = ex.Code });
            }
            catch (InvalidOperationException ex)
            {
                return NotFound(new { error = ex.Message });
            }
        }

        [HttpPost("{id:int}/delete")]
        [ValidateAntiForgeryToken]
        [RequirePermission(Permissions.AgentsManage)]
        public async Task<IActionResult> Delete(int id, CancellationToken ct)
        {
            var userId = _userManager.GetUserId(User) ?? string.Empty;
            try
            {
                await _agents.DeleteAsync(id, userId, ct);
                return Ok(new { deleted = true });
            }
            catch (AgentInvariantException ex)
            {
                return Conflict(new { error = ex.Message, code = ex.Code });
            }
            catch (InvalidOperationException ex)
            {
                return NotFound(new { error = ex.Message });
            }
        }

        [HttpPost("{id:int}/default")]
        [ValidateAntiForgeryToken]
        [RequirePermission(Permissions.AgentsManage)]
        public async Task<IActionResult> SetDefault(int id, CancellationToken ct)
        {
            var userId = _userManager.GetUserId(User) ?? string.Empty;
            try
            {
                var detail = await _agents.SetDefaultAsync(id, userId, ct);
                return Json(detail);
            }
            catch (AgentInvariantException ex)
            {
                return Conflict(new { error = ex.Message, code = ex.Code });
            }
            catch (InvalidOperationException ex)
            {
                return NotFound(new { error = ex.Message });
            }
        }

        public sealed class SetActiveDto { public bool IsActive { get; set; } }

        [HttpPost("{id:int}/active")]
        [ValidateAntiForgeryToken]
        [RequirePermission(Permissions.AgentsManage)]
        public async Task<IActionResult> SetActive(int id, [FromBody] SetActiveDto body, CancellationToken ct)
        {
            var userId = _userManager.GetUserId(User) ?? string.Empty;
            try
            {
                var detail = await _agents.SetActiveAsync(id, body?.IsActive ?? false, userId, ct);
                return Json(detail);
            }
            catch (AgentInvariantException ex)
            {
                return Conflict(new { error = ex.Message, code = ex.Code });
            }
            catch (InvalidOperationException ex)
            {
                return NotFound(new { error = ex.Message });
            }
        }

        [HttpPost("{id:int}/avatar")]
        [ValidateAntiForgeryToken]
        [RequirePermission(Permissions.AgentsManage)]
        [RequestSizeLimit(MaxAvatarBytes + 4096)]
        public async Task<IActionResult> UploadAvatar(int id, [FromForm] IFormFile avatar, CancellationToken ct)
        {
            if (avatar is null || avatar.Length == 0)
                return BadRequest(new { error = _localizer["Agents.Error.AvatarRequired"].Value });
            if (avatar.Length > MaxAvatarBytes)
                return BadRequest(new { error = _localizer["Agents.Error.AvatarTooLarge"].Value });

            var ext = Path.GetExtension(avatar.FileName)?.ToLowerInvariant();
            if (string.IsNullOrEmpty(ext) || !AllowedAvatarTypes.TryGetValue(ext, out var expectedMime))
                return BadRequest(new { error = _localizer["Agents.Error.AvatarFormat"].Value });
            if (!string.Equals(avatar.ContentType, expectedMime, StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { error = _localizer["Agents.Error.AvatarFormat"].Value });

            var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
            var dir = Path.Combine(webRoot, "uploads", "agents", id.ToString());
            Directory.CreateDirectory(dir);

            var fileName = $"{Guid.NewGuid():N}{ext}";
            var diskPath = Path.Combine(dir, fileName);
            await using (var fs = new FileStream(diskPath, FileMode.Create))
                await avatar.CopyToAsync(fs, ct);

            var relative = $"/uploads/agents/{id}/{fileName}";

            // Best-effort: clean up the previous avatar so the folder doesn't accumulate junk.
            var current = await _agents.GetAsync(id, ct);
            if (current is not null && !string.IsNullOrEmpty(current.AvatarPath))
            {
                var oldRel = current.AvatarPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
                var oldPath = Path.GetFullPath(Path.Combine(webRoot, oldRel));
                var allowedRoot = Path.GetFullPath(Path.Combine(webRoot, "uploads", "agents"));
                if (oldPath.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase) && System.IO.File.Exists(oldPath))
                {
                    try { System.IO.File.Delete(oldPath); }
                    catch (IOException ex) { _logger.LogWarning(ex, "Failed to delete old agent avatar {Path}", oldPath); }
                }
            }

            var userId = _userManager.GetUserId(User) ?? string.Empty;
            try
            {
                var detail = await _agents.SetAvatarAsync(id, relative, userId, ct);
                return Json(detail);
            }
            catch (InvalidOperationException ex)
            {
                return NotFound(new { error = ex.Message });
            }
        }

        // ── Picker (also useful from the chat panel — gated to view permission) ────────────

        [HttpGet("picker")]
        [RequirePermission(Permissions.AgentsView)]
        public async Task<IActionResult> Picker(CancellationToken ct)
        {
            var rows = await _agents.ListActiveForPickerAsync(ct);
            return Json(rows);
        }

        public sealed class ReassignDto { public int? AgentId { get; set; } }

        [HttpPost("/dashboard/conversations/{conversationId:int}/agent")]
        [ValidateAntiForgeryToken]
        [RequirePermission(Permissions.AgentsView)]
        public async Task<IActionResult> ReassignConversation(int conversationId, [FromBody] ReassignDto body, CancellationToken ct)
        {
            var userId = _userManager.GetUserId(User) ?? string.Empty;
            try
            {
                await _agents.ReassignConversationAsync(conversationId, body?.AgentId, userId, ct);
                return Ok(new { reassigned = true });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpPost("/dashboard/contacts/{contactId:int}/agent")]
        [ValidateAntiForgeryToken]
        [RequirePermission(Permissions.AgentsView)]
        public async Task<IActionResult> AssignContact(int contactId, [FromBody] ReassignDto body, CancellationToken ct)
        {
            var userId = _userManager.GetUserId(User) ?? string.Empty;
            try
            {
                var result = await _agents.AssignContactAsync(contactId, body?.AgentId, userId, ct);
                return Json(result);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        // ── helpers ────────────────────────────────────────────────────────

        private object BuildValidationPayload(AgentValidationException ex)
        {
            var fields = ex.Errors.ToDictionary(
                kv => kv.Key,
                kv => _localizer[kv.Value].Value);
            return new
            {
                error = _localizer["Agents.Error.Validation"].Value,
                code = "validation",
                fields
            };
        }
    }
}
