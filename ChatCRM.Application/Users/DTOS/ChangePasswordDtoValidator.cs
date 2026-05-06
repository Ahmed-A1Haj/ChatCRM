using FluentValidation;

namespace ChatCRM.Application.Users.DTOS
{
    public class ChangePasswordDtoValidator : AbstractValidator<ChangePasswordDto>
    {
        public ChangePasswordDtoValidator()
        {
            RuleFor(x => x.CurrentPassword)
                .NotEmpty().WithMessage("Enter your current password.");

            RuleFor(x => x.NewPassword)
                .NotEmpty().WithMessage("Create a new password.")
                .MinimumLength(10).WithMessage("Use at least 10 characters.")
                .Must(ContainUppercase).WithMessage("Include at least one uppercase letter.")
                .Must(ContainLowercase).WithMessage("Include at least one lowercase letter.")
                .Must(ContainDigit).WithMessage("Include at least one number.")
                .NotEqual(x => x.CurrentPassword).WithMessage("New password must be different from your current password.");

            RuleFor(x => x.ConfirmNewPassword)
                .NotEmpty().WithMessage("Confirm your new password.")
                .Equal(x => x.NewPassword)
                .WithMessage("The password confirmation does not match.");
        }

        private static bool ContainUppercase(string? password)
            => !string.IsNullOrWhiteSpace(password) && password.Any(char.IsUpper);

        private static bool ContainLowercase(string? password)
            => !string.IsNullOrWhiteSpace(password) && password.Any(char.IsLower);

        private static bool ContainDigit(string? password)
            => !string.IsNullOrWhiteSpace(password) && password.Any(char.IsDigit);
    }
}
