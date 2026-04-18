using System.ComponentModel.DataAnnotations;

namespace ChatCRM.Application.Users.DTOS
{
    public class ResetPasswordDto
    {
        public string Email { get; set; } = null!;

        public string Token { get; set; } = null!;

        [DataType(DataType.Password)]
        public string Password { get; set; } = null!;

        [DataType(DataType.Password)]
        [Display(Name = "Confirm password")]
        public string ConfirmPassword { get; set; } = null!;
    }
}
