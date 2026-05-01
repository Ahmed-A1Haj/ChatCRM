using System.Security.Claims;
using ChatCRM.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace ChatCRM.Infrastructure.Services
{
    public static class RoleSeeder
    {
        /// <summary>
        /// Idempotent seeder for the three default roles + their permission claims.
        /// Also promotes the very first registered user to Admin so the system is never lockless.
        /// </summary>
        public static async Task SeedAsync(
            RoleManager<IdentityRole> roleManager,
            UserManager<User> userManager,
            ILogger logger,
            CancellationToken cancellationToken = default)
        {
            // Default permission set per role.
            var presets = new Dictionary<string, string[]>
            {
                [Roles.Admin]   = Permissions.All,
                [Roles.Manager] = new[]
                {
                    Permissions.UsersView,
                    Permissions.ContactsView, Permissions.ContactsEdit, Permissions.ContactsDelete,
                    Permissions.ConversationsAssign, Permissions.ConversationsClose,
                    Permissions.ChannelsManage,
                    Permissions.SettingsView
                },
                [Roles.Agent] = new[]
                {
                    Permissions.ContactsView, Permissions.ContactsEdit,
                    Permissions.ConversationsAssign
                }
            };

            foreach (var (roleName, perms) in presets)
            {
                var role = await roleManager.FindByNameAsync(roleName);
                if (role is null)
                {
                    role = new IdentityRole(roleName);
                    var result = await roleManager.CreateAsync(role);
                    if (!result.Succeeded)
                    {
                        logger.LogError("Failed to create role {Role}: {Errors}", roleName, string.Join("; ", result.Errors.Select(e => e.Description)));
                        continue;
                    }
                    logger.LogInformation("[SEED] Created role {Role}", roleName);
                }

                // Sync permission claims: add any missing, but don't strip existing custom permissions.
                var existing = await roleManager.GetClaimsAsync(role);
                foreach (var perm in perms)
                {
                    if (existing.Any(c => c.Type == Permissions.ClaimType && c.Value == perm)) continue;
                    await roleManager.AddClaimAsync(role, new Claim(Permissions.ClaimType, perm));
                }
            }

            // Backfill IsActive for accounts that predate the column. Without this, anyone created
            // before AddUserManagementFields ran has IsActive=0 and is silently denied by the
            // permission handler — which looks like an RBAC bug but is actually a default-value bug.
            var inactiveLegacy = userManager.Users.Where(u => !u.IsActive).ToList();
            foreach (var u in inactiveLegacy)
            {
                u.IsActive = true;
                await userManager.UpdateAsync(u);
            }
            if (inactiveLegacy.Count > 0)
                logger.LogInformation("[SEED] Activated {Count} legacy user(s) with default IsActive=false.", inactiveLegacy.Count);

            // Promote first user to Admin if no admin exists yet.
            var admins = await userManager.GetUsersInRoleAsync(Roles.Admin);
            if (admins.Count == 0)
            {
                var first = userManager.Users.OrderBy(u => u.CreatedAt).FirstOrDefault();
                if (first is not null)
                {
                    var add = await userManager.AddToRoleAsync(first, Roles.Admin);
                    if (add.Succeeded)
                        logger.LogInformation("[SEED] Promoted first user {Email} to Admin", first.Email);
                }
            }
        }
    }
}
