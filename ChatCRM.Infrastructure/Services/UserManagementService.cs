using System.Security.Claims;
using ChatCRM.Application.Interfaces;
using ChatCRM.Application.Users.DTOS;
using ChatCRM.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ChatCRM.Infrastructure.Services
{
    public class UserManagementService : IUserManagementService
    {
        private readonly UserManager<User> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly ILogger<UserManagementService> _logger;

        public UserManagementService(
            UserManager<User> userManager,
            RoleManager<IdentityRole> roleManager,
            ILogger<UserManagementService> logger)
        {
            _userManager = userManager;
            _roleManager = roleManager;
            _logger = logger;
        }

        public async Task<List<UserListItemDto>> ListAsync(string? search = null, CancellationToken cancellationToken = default)
        {
            var users = _userManager.Users.AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim().ToLower();
                users = users.Where(u =>
                    (u.Email != null && u.Email.ToLower().Contains(s)) ||
                    (u.FirstName != null && u.FirstName.ToLower().Contains(s)) ||
                    (u.LastName != null && u.LastName.ToLower().Contains(s)));
            }

            var list = await users.OrderBy(u => u.CreatedAt).ToListAsync(cancellationToken);
            var result = new List<UserListItemDto>(list.Count);

            foreach (var u in list)
            {
                var roles = await _userManager.GetRolesAsync(u);
                result.Add(MapDto(u, roles));
            }

            return result;
        }

        public async Task<UserListItemDto?> GetAsync(string id, CancellationToken cancellationToken = default)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user is null) return null;
            var roles = await _userManager.GetRolesAsync(user);
            return MapDto(user, roles);
        }

        public async Task<UserListItemDto> CreateAsync(CreateUserDto dto, CancellationToken cancellationToken = default)
        {
            var email = (dto.Email ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(email))
                throw new InvalidOperationException("Email is required.");
            if (string.IsNullOrWhiteSpace(dto.Password))
                throw new InvalidOperationException("Password is required.");

            if (await _userManager.FindByEmailAsync(email) is not null)
                throw new InvalidOperationException($"A user with email \"{email}\" already exists.");

            var user = new User
            {
                UserName = email,
                Email = email,
                FirstName = dto.FirstName?.Trim(),
                LastName = dto.LastName?.Trim(),
                EmailConfirmed = true,        // admin-created accounts skip the email verification ceremony
                IsActive = dto.IsActive,
                CreatedAt = DateTime.UtcNow
            };

            var create = await _userManager.CreateAsync(user, dto.Password);
            if (!create.Succeeded)
                throw new InvalidOperationException(string.Join("; ", create.Errors.Select(e => e.Description)));

            await EnsureRoleAsync(user, dto.Role);

            return (await GetAsync(user.Id, cancellationToken))!;
        }

        public async Task<UserListItemDto> UpdateAsync(UpdateUserDto dto, CancellationToken cancellationToken = default)
        {
            var user = await _userManager.FindByIdAsync(dto.Id)
                ?? throw new InvalidOperationException($"User {dto.Id} not found.");

            var email = (dto.Email ?? string.Empty).Trim().ToLowerInvariant();
            if (!string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase))
            {
                var other = await _userManager.FindByEmailAsync(email);
                if (other is not null && other.Id != user.Id)
                    throw new InvalidOperationException("That email is already used by another user.");

                user.Email = email;
                user.UserName = email;
                user.NormalizedEmail = email.ToUpperInvariant();
                user.NormalizedUserName = email.ToUpperInvariant();
            }

            user.FirstName = dto.FirstName?.Trim();
            user.LastName  = dto.LastName?.Trim();
            user.IsActive  = dto.IsActive;

            var update = await _userManager.UpdateAsync(user);
            if (!update.Succeeded)
                throw new InvalidOperationException(string.Join("; ", update.Errors.Select(e => e.Description)));

            // Reset password if a new one was supplied.
            if (!string.IsNullOrWhiteSpace(dto.NewPassword))
            {
                var token = await _userManager.GeneratePasswordResetTokenAsync(user);
                var reset = await _userManager.ResetPasswordAsync(user, token, dto.NewPassword);
                if (!reset.Succeeded)
                    throw new InvalidOperationException(string.Join("; ", reset.Errors.Select(e => e.Description)));
            }

            await EnsureRoleAsync(user, dto.Role);

            return (await GetAsync(user.Id, cancellationToken))!;
        }

        public async Task SetActiveAsync(string id, bool isActive, CancellationToken cancellationToken = default)
        {
            var user = await _userManager.FindByIdAsync(id)
                ?? throw new InvalidOperationException($"User {id} not found.");

            if (!isActive)
            {
                // Don't let an admin deactivate the last remaining admin (or themselves if they're the only one).
                var roles = await _userManager.GetRolesAsync(user);
                if (roles.Contains(Roles.Admin))
                {
                    var activeAdmins = (await _userManager.GetUsersInRoleAsync(Roles.Admin))
                        .Count(u => u.IsActive && u.Id != id);
                    if (activeAdmins == 0)
                        throw new InvalidOperationException("You can't deactivate the last active admin.");
                }
            }

            user.IsActive = isActive;
            // Lock out forever (or unlock) so existing cookies stop working when deactivated.
            await _userManager.SetLockoutEndDateAsync(user, isActive ? null : DateTimeOffset.UtcNow.AddYears(100));
            await _userManager.UpdateAsync(user);
        }

        public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
        {
            var user = await _userManager.FindByIdAsync(id)
                ?? throw new InvalidOperationException($"User {id} not found.");

            // Don't let an admin delete the last remaining admin.
            var adminCount = (await _userManager.GetUsersInRoleAsync(Roles.Admin)).Count;
            var roles = await _userManager.GetRolesAsync(user);
            if (roles.Contains(Roles.Admin) && adminCount <= 1)
                throw new InvalidOperationException("You can't delete the last admin.");

            var result = await _userManager.DeleteAsync(user);
            if (!result.Succeeded)
                throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));
        }

        // ── Helpers ──────────────────────────────────────────────────────

        private async Task EnsureRoleAsync(User user, string desiredRole)
        {
            if (string.IsNullOrWhiteSpace(desiredRole)) return;

            var current = await _userManager.GetRolesAsync(user);

            // Remove all previous roles (single-role assignment for simplicity).
            if (current.Count > 0)
                await _userManager.RemoveFromRolesAsync(user, current);

            if (await _roleManager.RoleExistsAsync(desiredRole))
                await _userManager.AddToRoleAsync(user, desiredRole);
        }

        private static UserListItemDto MapDto(User u, IList<string> roles) => new()
        {
            Id = u.Id,
            Email = u.Email ?? string.Empty,
            FirstName = u.FirstName,
            LastName = u.LastName,
            Roles = roles.ToList(),
            IsActive = u.IsActive,
            EmailConfirmed = u.EmailConfirmed,
            CreatedAt = u.CreatedAt,
            ProfileImagePath = u.ProfileImagePath
        };
    }

    public class RoleManagementService : IRoleManagementService
    {
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly UserManager<User> _userManager;

        public RoleManagementService(RoleManager<IdentityRole> roleManager, UserManager<User> userManager)
        {
            _roleManager = roleManager;
            _userManager = userManager;
        }

        public async Task<List<RoleListItemDto>> ListAsync(CancellationToken cancellationToken = default)
        {
            var roles = await _roleManager.Roles.OrderBy(r => r.Name).ToListAsync(cancellationToken);
            var result = new List<RoleListItemDto>(roles.Count);
            foreach (var role in roles)
            {
                var claims = await _roleManager.GetClaimsAsync(role);
                var users  = await _userManager.GetUsersInRoleAsync(role.Name!);
                result.Add(new RoleListItemDto
                {
                    Id = role.Id,
                    Name = role.Name ?? string.Empty,
                    Permissions = claims.Where(c => c.Type == Permissions.ClaimType).Select(c => c.Value).ToList(),
                    UserCount = users.Count
                });
            }
            return result;
        }

        public async Task<RoleListItemDto?> GetAsync(string id, CancellationToken cancellationToken = default)
        {
            var role = await _roleManager.FindByIdAsync(id);
            if (role is null) return null;
            var claims = await _roleManager.GetClaimsAsync(role);
            var users  = await _userManager.GetUsersInRoleAsync(role.Name!);
            return new RoleListItemDto
            {
                Id = role.Id,
                Name = role.Name ?? string.Empty,
                Permissions = claims.Where(c => c.Type == Permissions.ClaimType).Select(c => c.Value).ToList(),
                UserCount = users.Count
            };
        }

        public async Task<RoleListItemDto> SaveAsync(SaveRoleDto dto, CancellationToken cancellationToken = default)
        {
            var name = dto.Name.Trim();
            if (string.IsNullOrWhiteSpace(name))
                throw new InvalidOperationException("Role name is required.");

            IdentityRole? role;
            if (string.IsNullOrEmpty(dto.Id))
            {
                if (await _roleManager.RoleExistsAsync(name))
                    throw new InvalidOperationException($"Role \"{name}\" already exists.");
                role = new IdentityRole(name);
                var createResult = await _roleManager.CreateAsync(role);
                if (!createResult.Succeeded)
                    throw new InvalidOperationException(string.Join("; ", createResult.Errors.Select(e => e.Description)));
            }
            else
            {
                role = await _roleManager.FindByIdAsync(dto.Id)
                    ?? throw new InvalidOperationException("Role not found.");
                if (!string.Equals(role.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    role.Name = name;
                    role.NormalizedName = name.ToUpperInvariant();
                    await _roleManager.UpdateAsync(role);
                }
            }

            // Sync permission claims to match dto.Permissions exactly.
            var existing = (await _roleManager.GetClaimsAsync(role))
                .Where(c => c.Type == Permissions.ClaimType)
                .ToList();

            var desired = dto.Permissions.Where(p => Permissions.All.Contains(p)).ToHashSet();

            foreach (var claim in existing.Where(c => !desired.Contains(c.Value)))
                await _roleManager.RemoveClaimAsync(role, claim);

            foreach (var perm in desired.Where(p => !existing.Any(c => c.Value == p)))
                await _roleManager.AddClaimAsync(role, new Claim(Permissions.ClaimType, perm));

            return (await GetAsync(role.Id, cancellationToken))!;
        }

        public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
        {
            var role = await _roleManager.FindByIdAsync(id)
                ?? throw new InvalidOperationException("Role not found.");

            // Built-in roles can't be deleted.
            if (Roles.All.Contains(role.Name))
                throw new InvalidOperationException("Built-in roles cannot be deleted.");

            var users = await _userManager.GetUsersInRoleAsync(role.Name!);
            if (users.Count > 0)
                throw new InvalidOperationException($"This role has {users.Count} user(s). Reassign them first.");

            var result = await _roleManager.DeleteAsync(role);
            if (!result.Succeeded)
                throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));
        }
    }
}
