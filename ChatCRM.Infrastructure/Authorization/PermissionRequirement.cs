using System.Security.Claims;
using ChatCRM.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ChatCRM.Infrastructure.Authorization
{
    /// <summary>Marker requirement carrying the permission key the user must hold.</summary>
    public class PermissionRequirement : IAuthorizationRequirement
    {
        public string Permission { get; }
        public PermissionRequirement(string permission) => Permission = permission;
    }

    /// <summary>
    /// Resolves the user's permission set from the claims of every role they belong to.
    /// Cached per request via the role manager (which itself talks to EF Core).
    /// </summary>
    public class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
    {
        private readonly UserManager<User> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;

        public PermissionAuthorizationHandler(UserManager<User> userManager, RoleManager<IdentityRole> roleManager)
        {
            _userManager = userManager;
            _roleManager = roleManager;
        }

        protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
        {
            if (!context.User.Identity?.IsAuthenticated ?? true) return;

            var user = await _userManager.GetUserAsync(context.User);
            if (user is null || !user.IsActive) return;

            var roles = await _userManager.GetRolesAsync(user);
            foreach (var roleName in roles)
            {
                var role = await _roleManager.FindByNameAsync(roleName);
                if (role is null) continue;
                var claims = await _roleManager.GetClaimsAsync(role);
                if (claims.Any(c => c.Type == Permissions.ClaimType && c.Value == requirement.Permission))
                {
                    context.Succeed(requirement);
                    return;
                }
            }
        }
    }

    /// <summary>
    /// Convenience attribute so controllers/actions can write [RequirePermission("users.manage")]
    /// instead of [Authorize(Policy = "permission:users.manage")].
    /// </summary>
    public class RequirePermissionAttribute : TypeFilterAttribute
    {
        public RequirePermissionAttribute(string permission) : base(typeof(PermissionFilter))
        {
            Arguments = new object[] { permission };
        }

        private class PermissionFilter : IAsyncAuthorizationFilter
        {
            private readonly IAuthorizationService _authorizationService;
            private readonly string _permission;

            public PermissionFilter(IAuthorizationService authorizationService, string permission)
            {
                _authorizationService = authorizationService;
                _permission = permission;
            }

            public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
            {
                if (context.HttpContext.User.Identity?.IsAuthenticated != true)
                {
                    context.Result = new ChallengeResult();
                    return;
                }

                var result = await _authorizationService.AuthorizeAsync(
                    context.HttpContext.User, null, new PermissionRequirement(_permission));

                if (!result.Succeeded)
                {
                    context.Result = new ForbidResult();
                }
            }
        }
    }
}
