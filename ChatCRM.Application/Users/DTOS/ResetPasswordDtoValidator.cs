using FluentValidation;

namespace ChatCRM.Application.Users.DTOS
{
    public class ResetPasswordDtoValidator : AbstractValidator<ResetPasswordDto>
    {
        public ResetPasswordDtoValidator()
        {
            RuleFor(x => x.Email)
                .NotEmpty().WithMessage("Email is required.")
                .EmailAddress().WithMessage("Enter a valid email address.");

            RuleFor(x => x.Token)
                .NotEmpty().WithMessage("This reset link is invalid or incomplete.");

            RuleFor(x => x.Password)
                .NotEmpty().WithMessage("Create a new password.")
                .MinimumLength(10).WithMessage("Use at least 10 characters.")
                .Must(ContainUppercase).WithMessage("Include at least one uppercase letter.")
                .Must(ContainLowercase).WithMessage("Include at least one lowercase letter.")
                .Must(ContainDigit).WithMessage("Include at least one number.");

            RuleFor(x => x.ConfirmPassword)
                .NotEmpty().WithMessage("Confirm your new password.")
                .Equal(x => x.Password)
                .WithMessage("The password confirmation does not match.");
        }

        private static bool ContainUppercase(string? password)
        {
            return !string.IsNullOrWhiteSpace(password) && password.Any(char.IsUpper);
        }

        private static bool ContainLowercase(string? password)
        {
            return !string.IsNullOrWhiteSpace(password) && password.Any(char.IsLower);
        }

        private static bool ContainDigit(string? password)
        {
            return !string.IsNullOrWhiteSpace(password) && password.Any(char.IsDigit);
        }
    }
}
