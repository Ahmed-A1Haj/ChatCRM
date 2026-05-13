using ChatCRM.Application.Interfaces;
using ChatCRM.Application.Templates;
using ChatCRM.Application.Templates.DTOs;
using ChatCRM.Application.Templates.Exceptions;
using ChatCRM.Domain.Entities;
using ChatCRM.Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace ChatCRM.MVC.Controllers
{
    /// <summary>
    /// Templates manager. Workspace-wide library, gated by the <c>TemplatesView/Create/
    /// Submit/Delete</c> permissions. All non-GET endpoints validate antiforgery via
    /// the global filter; the create/update form bodies arrive as JSON from the page's
    /// fetch() calls.
    /// </summary>
    [Authorize]
    public sealed class TemplatesController : Controller
    {
        private readonly ITemplateService _service;
        private readonly UserManager<User> _userManager;
        private readonly IStringLocalizer _localizer;
        private readonly ILogger<TemplatesController> _logger;

        public TemplatesController(
            ITemplateService service,
            UserManager<User> userManager,
            IStringLocalizer localizer,
            ILogger<TemplatesController> logger)
        {
            _service = service;
            _userManager = userManager;
            _localizer = localizer;
            _logger = logger;
        }

        [HttpGet("/dashboard/templates")]
        [RequirePermission(Permissions.TemplatesView)]
        public IActionResult Index()
        {
            ViewBag.Languages = MetaTemplateLanguages.All;
            return View();
        }

        [HttpGet("/dashboard/templates/list")]
        [RequirePermission(Permissions.TemplatesView)]
        public async Task<IActionResult> List([FromQuery] bool archived, CancellationToken ct)
        {
            var rows = await _service.ListAsync(archived, ct);
            return Json(rows);
        }

        [HttpGet("/dashboard/templates/{id:int}")]
        [RequirePermission(Permissions.TemplatesView)]
        public async Task<IActionResult> Detail(int id, CancellationToken ct)
        {
            var detail = await _service.GetAsync(id, ct);
            return detail is null ? NotFound() : Json(detail);
        }

        [HttpGet("/dashboard/templates/state")]
        [RequirePermission(Permissions.TemplatesView)]
        public async Task<IActionResult> State(CancellationToken ct)
        {
            var state = await _service.GetProviderStateAsync(ct);
            return Json(state);
        }

        [HttpGet("/dashboard/templates/approved")]
        public async Task<IActionResult> Approved(CancellationToken ct)
        {
            // Intentionally not gated by TemplatesView — agents who can send messages should
            // be able to *see* approved templates, even if they can't author/submit them.
            var rows = await _service.ListApprovedForPickerAsync(ct);
            return Json(rows);
        }

        [HttpPost("/dashboard/templates/draft")]
        [ValidateAntiForgeryToken]
        [RequirePermission(Permissions.TemplatesCreate)]
        public async Task<IActionResult> CreateDraft([FromBody] CreateTemplateDto dto, CancellationToken ct)
        {
            var userId = _userManager.GetUserId(User) ?? string.Empty;
            try
            {
                var detail = await _service.CreateDraftAsync(dto, userId, ct);
                return Json(detail);
            }
            catch (TemplateValidationException ex)
            {
                return UnprocessableEntity(BuildValidationPayload(ex));
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Template draft creation failed.");
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpPost("/dashboard/templates/{id:int}/draft")]
        [ValidateAntiForgeryToken]
        [RequirePermission(Permissions.TemplatesCreate)]
        public async Task<IActionResult> UpdateDraft(int id, [FromBody] UpdateTemplateDto dto, CancellationToken ct)
        {
            var userId = _userManager.GetUserId(User) ?? string.Empty;
            try
            {
                var detail = await _service.UpdateDraftAsync(id, dto, userId, ct);
                return Json(detail);
            }
            catch (TemplateValidationException ex)
            {
                return UnprocessableEntity(BuildValidationPayload(ex));
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        public sealed class SubmitRequest
        {
            public int? SubmittedViaInstanceId { get; set; }
        }

        [HttpPost("/dashboard/templates/{id:int}/submit")]
        [ValidateAntiForgeryToken]
        [RequirePermission(Permissions.TemplatesSubmit)]
        public async Task<IActionResult> Submit(int id, [FromBody] SubmitRequest? body, CancellationToken ct)
        {
            var userId = _userManager.GetUserId(User) ?? string.Empty;
            try
            {
                var detail = await _service.SubmitAsync(id, body?.SubmittedViaInstanceId, userId, ct);
                return Json(detail);
            }
            catch (TemplateProviderNotConfiguredException)
            {
                return StatusCode(409, new { error = _localizer["Templates.Error.NotConfigured"].Value, code = "not_configured" });
            }
            catch (TemplateAuthenticationException ex)
            {
                _logger.LogWarning(ex, "Template submit auth failed.");
                return StatusCode(401, new { error = _localizer["Templates.Error.AuthFailed"].Value, code = "auth_failed" });
            }
            catch (TemplateRateLimitException ex)
            {
                return StatusCode(429, new
                {
                    error = _localizer["Templates.Error.RateLimited"].Value,
                    code = "rate_limited",
                    retryAfterMinutes = (int)Math.Ceiling(ex.RetryAfter.TotalMinutes)
                });
            }
            catch (TemplateValidationException ex)
            {
                return UnprocessableEntity(BuildValidationPayload(ex));
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpPost("/dashboard/templates/{id:int}/delete")]
        [ValidateAntiForgeryToken]
        [RequirePermission(Permissions.TemplatesDelete)]
        public async Task<IActionResult> Delete(int id, CancellationToken ct)
        {
            var userId = _userManager.GetUserId(User) ?? string.Empty;
            try
            {
                await _service.SoftDeleteAsync(id, userId, ct);
                return Ok(new { archived = true });
            }
            catch (TemplateAuthenticationException)
            {
                return StatusCode(401, new { error = _localizer["Templates.Error.AuthFailed"].Value, code = "auth_failed" });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpPost("/dashboard/templates/sync")]
        [ValidateAntiForgeryToken]
        [RequirePermission(Permissions.TemplatesSubmit)]
        public async Task<IActionResult> Sync(CancellationToken ct)
        {
            var userId = _userManager.GetUserId(User) ?? string.Empty;
            try
            {
                var result = await _service.SyncFromMetaAsync(userId, ct);
                return Json(result);
            }
            catch (TemplateProviderNotConfiguredException)
            {
                return StatusCode(409, new { error = _localizer["Templates.Error.NotConfigured"].Value, code = "not_configured" });
            }
            catch (TemplateAuthenticationException)
            {
                return StatusCode(401, new { error = _localizer["Templates.Error.AuthFailed"].Value, code = "auth_failed" });
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "Template sync transport failure.");
                return StatusCode(502, new { error = _localizer["Templates.Error.SyncFailed"].Value, code = "sync_failed" });
            }
        }

        // ── helpers ─────────────────────────────────────────────────────────

        private object BuildValidationPayload(TemplateValidationException ex)
        {
            // Translate each field error key once on the server so the client doesn't need
            // to know the keyspace. Keys come from TemplateContentValidator.
            var fields = ex.Errors.ToDictionary(
                kv => kv.Key,
                kv => _localizer[kv.Value].Value);
            return new
            {
                error = _localizer["Templates.Error.Validation"].Value,
                code = "validation",
                fields
            };
        }
    }
}
