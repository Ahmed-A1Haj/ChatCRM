using FluentValidation;

namespace ChatCRM.Application.Users.DTOS
{
    public class UpdateProfileDtoValidator : AbstractValidator<UpdateProfileDto>
    {
        public UpdateProfileDtoValidator()
        {
            RuleFor(x => x.FirstName)
                .MaximumLength(100).WithMessage("First name must be 100 characters or fewer.");

            RuleFor(x => x.LastName)
                .MaximumLength(100).WithMessage("Last name must be 100 characters or fewer.");

            RuleFor(x => x.Email)
                .NotEmpty().WithMessage("Enter your email address.")
                .EmailAddress().WithMessage("Enter a valid email address.");

            RuleFor(x => x.PhoneNumber)
                .MaximumLength(25).WithMessage("Phone number must be 25 characters or fewer.")
                .Matches(@"^\+?[0-9\s\-\(\)]*$")
                .When(x => !string.IsNullOrWhiteSpace(x.PhoneNumber))
                .WithMessage("Phone number can only contain digits, spaces, parentheses, dashes, and an optional leading +.");
        }
    }
}
