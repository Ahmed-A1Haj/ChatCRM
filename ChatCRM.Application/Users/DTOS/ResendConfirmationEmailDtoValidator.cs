using FluentValidation;

namespace ChatCRM.Application.Users.DTOS
{
    public class ResendConfirmationEmailDtoValidator : AbstractValidator<ResendConfirmationEmailDto>
    {
        public ResendConfirmationEmailDtoValidator()
        {
            RuleFor(x => x.Email)
                .NotEmpty().WithMessage("Enter the email address you used to register.")
                .EmailAddress().WithMessage("Enter a valid email address.");
        }
    }
}
