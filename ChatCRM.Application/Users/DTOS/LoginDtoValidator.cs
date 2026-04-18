using FluentValidation;

namespace ChatCRM.Application.Users.DTOS
{
    public class LoginDtoValidator : AbstractValidator<LoginDto>
    {
        public LoginDtoValidator()
        {
            RuleFor(x => x.Email)
                .NotEmpty().WithMessage("Enter your email address.")
                .EmailAddress().WithMessage("Enter a valid email address.");

            RuleFor(x => x.Password)
                .NotEmpty().WithMessage("Enter your password.")
                .MinimumLength(8).WithMessage("Password must be at least 8 characters.");
        }
    }
}
