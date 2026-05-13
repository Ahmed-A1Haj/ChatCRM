using ChatCRM.Application.Interfaces;
using ChatCRM.Domain.Entities;
using ChatCRM.Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChatCRM.MVC.Controllers
{
    /// <summary>
    /// Platform-operator dashboard. Gated by <see cref="Permissions.PlatformAdmin"/> — distinct
    /// from workspace-level billing admin. Read-only in v1; adjustment UI lives on the existing
    /// per-workspace billing page.
    /// </summary>
    [Authorize]
    [Route("/dashboard/platform-admin")]
    public sealed class PlatformAdminController : Controller
    {
        private readonly IPlatformAdminService _admin;

        public PlatformAdminController(IPlatformAdminService admin)
        {
            _admin = admin;
        }

        [HttpGet("")]
        [RequirePermission(Permissions.PlatformAdmin)]
        public IActionResult Index() => View();

        [HttpGet("overview")]
        [RequirePermission(Permissions.PlatformAdmin)]
        public async Task<IActionResult> Overview([FromQuery] string? range, CancellationToken ct)
        {
            var (from, to) = ResolveRange(range);
            var data = await _admin.GetOverviewAsync(from, to, topN: 10, ct);
            return Json(data);
        }

        private static (DateTime From, DateTime To) ResolveRange(string? range)
        {
            var nowUtc = DateTime.UtcNow;
            return (range ?? "mtd").ToLowerInvariant() switch
            {
                "7d"  => (nowUtc.Date.AddDays(-7), nowUtc.Date.AddDays(1)),
                "30d" => (nowUtc.Date.AddDays(-30), nowUtc.Date.AddDays(1)),
                "ytd" => (new DateTime(nowUtc.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc), nowUtc.Date.AddDays(1)),
                _     => (new DateTime(nowUtc.Year, nowUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc), nowUtc.Date.AddDays(1))
            };
        }
    }
}
