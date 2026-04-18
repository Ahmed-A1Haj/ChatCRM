using System.ComponentModel.DataAnnotations;

namespace ChatCRM.Application.Users.DTOS
{
    public class ResendConfirmationEmailDto
    {
        [Display(Name = "Email address")]
        public string Email { get; set; } = null!;
    }
}
