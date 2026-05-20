namespace ChatCRM.Application.Users.DTOS
{
    public class UserListItemDto
    {
        public string Id { get; set; } = string.Empty;
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string Email { get; set; } = string.Empty;
        public List<string> Roles { get; set; } = new();
        public bool IsActive { get; set; }
        public bool EmailConfirmed { get; set; }
        public DateTime CreatedAt { get; set; }
        public string? ProfileImagePath { get; set; }
    }

    public class CreateUserDto
    {
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string Role { get; set; } = "Agent";
        public bool IsActive { get; set; } = true;
    }

    public class UpdateUserDto
    {
        public string Id { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string Role { get; set; } = "Agent";
        public bool IsActive { get; set; } = true;
        public string? NewPassword { get; set; }
    }

    public class RoleListItemDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public List<string> Permissions { get; set; } = new();
        public int UserCount { get; set; }
    }

    public class SaveRoleDto
    {
        public string? Id { get; set; }            // null when creating
        public string Name { get; set; } = string.Empty;
        public List<string> Permissions { get; set; } = new();
    }
}
