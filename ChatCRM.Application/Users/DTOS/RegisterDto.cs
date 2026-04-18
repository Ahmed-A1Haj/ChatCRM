using System.ComponentModel.DataAnnotations;

namespace ChatCRM.Application.Users.DTOS
{
    public class RegisterDto
    {
        [Display(Name = "Email address")]
        public string Email { get; set; } = null!;

        [DataType(DataType.Password)]
        public string Password { get; set; } = null!;

        [DataType(DataType.Password)]
        [Display(Name = "Confirm password")]
        public string ConfirmPassword { get; set; } = null!;
    }
}
