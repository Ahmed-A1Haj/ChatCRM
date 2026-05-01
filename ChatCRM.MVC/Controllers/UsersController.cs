using ChatCRM.Application.Interfaces;
using ChatCRM.Application.Users.DTOS;
using ChatCRM.Domain.Entities;
using ChatCRM.Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChatCRM.MVC.Controllers
{
    [Authorize]
    public class UsersController : Controller
    {
        private readonly IUserManagementService _users;
        private readonly IRoleManagementService _roles;
        private readonly ILogger<UsersController> _logger;

        public UsersController(IUserManagementService users, IRoleManagementService roles, ILogger<UsersController> logger)
        {
            _users = users;
            _roles = roles;
            _logger = logger;
        }

        // ── Page ────────────────────────────────────────────────────────

        [HttpGet("/dashboard/settings/users")]
        [RequirePermission(Permissions.UsersView)]
        public async Task<IActionResult> Index(CancellationToken cancellationToken)
        {
            ViewBag.Roles = await _roles.ListAsync(cancellationToken);
            return View();
        }

        // ── REST ────────────────────────────────────────────────────────

        [HttpGet("/api/users")]
        [RequirePermission(Permissions.UsersView)]
        public async Task<IActionResult> List([FromQuery] string? search, CancellationToken cancellationToken)
            => Ok(await _users.ListAsync(search, cancellationToken));

        [HttpGet("/api/users/{id}")]
        [RequirePermission(Permissions.UsersView)]
        public async Task<IActionResult> Get(string id, CancellationToken cancellationToken)
        {
            var u = await _users.GetAsync(id, cancellationToken);
            return u is null ? NotFound() : Ok(u);
        }

        [HttpPost("/api/users")]
        [RequirePermission(Permissions.UsersManage)]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([FromBody] CreateUserDto dto, CancellationToken cancellationToken)
        {
            try
            {
                var created = await _users.CreateAsync(dto, cancellationToken);
                return Ok(created);
            }
            catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
        }

        [HttpPut("/api/users/{id}")]
        [RequirePermission(Permissions.UsersManage)]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Update(string id, [FromBody] UpdateUserDto dto, CancellationToken cancellationToken)
        {
            dto.Id = id;
            try
            {
                var updated = await _users.UpdateAsync(dto, cancellationToken);
                return Ok(updated);
            }
            catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
        }

        [HttpPost("/api/users/{id}/active")]
        [RequirePermission(Permissions.UsersManage)]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetActive(string id, [FromBody] SetActiveBody body, CancellationToken cancellationToken)
        {
            try
            {
                await _users.SetActiveAsync(id, body.IsActive, cancellationToken);
                return Ok(new { active = body.IsActive });
            }
            catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
        }

        [HttpDelete("/api/users/{id}")]
        [RequirePermission(Permissions.UsersManage)]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken)
        {
            try
            {
                await _users.DeleteAsync(id, cancellationToken);
                return Ok(new { deleted = true });
            }
            catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
        }

        public class SetActiveBody { public bool IsActive { get; set; } }
    }
}
