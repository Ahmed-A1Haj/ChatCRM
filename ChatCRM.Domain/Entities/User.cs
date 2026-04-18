using Microsoft.AspNetCore.Identity;

namespace ChatCRM.Domain.Entities
{
    public class User : IdentityUser
    {
        public string? FirstName { get; set; }

        public string? LastName { get; set; }

        public string? ProfileImagePath { get; set; }
    }
}
