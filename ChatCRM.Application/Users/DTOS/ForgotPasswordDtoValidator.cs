using FluentValidation;

namespace ChatCRM.Application.Users.DTOS
{
    public class ForgotPasswordDtoValidator : AbstractValidator<ForgotPasswordDto>
    {
        public ForgotPasswordDtoValidator()
        {
            RuleFor(x => x.Email)
                .NotEmpty().WithMessage("Enter the email address linked to your account.")
                .EmailAddress().WithMessage("Enter a valid email address.");
        }
    }
}
