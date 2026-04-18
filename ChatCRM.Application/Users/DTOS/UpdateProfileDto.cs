using System.ComponentModel.DataAnnotations;

namespace ChatCRM.Application.Users.DTOS
{
    public class UpdateProfileDto
    {
        [Display(Name = "First name")]
        public string? FirstName { get; set; }

        [Display(Name = "Last name")]
        public string? LastName { get; set; }

        [Display(Name = "Email address")]
        public string Email { get; set; } = null!;

        [Display(Name = "Phone number")]
        public string? PhoneNumber { get; set; }

        [Display(Name = "Remove current photo")]
        public bool RemoveProfileImage { get; set; }

        public string? ProfileImagePath { get; set; }

        public bool EmailConfirmed { get; set; }
    }
}
