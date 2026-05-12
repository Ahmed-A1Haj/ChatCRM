using ChatCRM.Application.Billing.DTOs;
using ChatCRM.Application.Interfaces;
using ChatCRM.Domain.Entities;
using ChatCRM.Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChatCRM.MVC.Controllers
{
    /// <summary>
    /// Audit log surface — read-only paged view over <c>BillingAuditLog</c>. Gated by
    /// <see cref="Permissions.PlatformAdmin"/> because the log spans every workspace.
    /// </summary>
    [Authorize]
    [Route("/dashboard/audit")]
    public sealed class AuditController : Controller
    {
        private readonly IAuditLogService _audit;

        public AuditController(IAuditLogService audit) => _audit = audit;

        [HttpGet("")]
        [RequirePermission(Permissions.PlatformAdmin)]
        public IActionResult Index() => View();

        [HttpGet("query")]
        [RequirePermission(Permissions.PlatformAdmin)]
        public async Task<IActionResult> Query([FromQuery] AuditLogQuery query, CancellationToken ct)
        {
            var page = await _audit.QueryAsync(query, ct);
            return Json(page);
        }

        [HttpGet("filters")]
        [RequirePermission(Permissions.PlatformAdmin)]
        public async Task<IActionResult> Filters(CancellationToken ct)
        {
            var actions = await _audit.ListDistinctActionsAsync(ct);
            var entityTypes = await _audit.ListDistinctEntityTypesAsync(ct);
            return Json(new { actions, entityTypes });
        }
    }
}
