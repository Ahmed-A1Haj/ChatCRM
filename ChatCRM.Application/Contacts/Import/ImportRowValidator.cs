using System.Text.RegularExpressions;
using FluentValidation;

namespace ChatCRM.Application.Contacts.Import
{
    /// <summary>
    /// Phone Number is the only required field — it's the unique key on WhatsAppContacts.
    /// Everything else is optional and only validated when present.
    /// </summary>
    public class ImportRowValidator : AbstractValidator<ImportRowDto>
    {
        // RFC-5322 simplified: tolerant enough for real-world contact data.
        private static readonly Regex EmailRegex = new(
            @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public ImportRowValidator()
        {
            RuleFor(r => r.PhoneNumber)
                .Must(p => !string.IsNullOrWhiteSpace(p))
                    .WithMessage("Phone Number is required.")
                .Must(p => PhoneNormalizer.Normalize(p).Length >= 6)
                    .WithMessage("Phone Number must contain at least 6 digits.")
                .Must(p => PhoneNormalizer.Normalize(p).Length <= 30)
                    .WithMessage("Phone Number is too long (max 30 digits).");

            RuleFor(r => r.FirstName)
                .MaximumLength(100).When(r => !string.IsNullOrWhiteSpace(r.FirstName))
                    .WithMessage("First Name is too long (max 100 chars).");

            RuleFor(r => r.LastName)
                .MaximumLength(100).When(r => !string.IsNullOrWhiteSpace(r.LastName))
                    .WithMessage("Last Name is too long (max 100 chars).");

            RuleFor(r => r.Email)
                .Must(e => string.IsNullOrWhiteSpace(e) || EmailRegex.IsMatch(e.Trim()))
                    .WithMessage("Email is not a valid address.")
                .MaximumLength(254).When(r => !string.IsNullOrWhiteSpace(r.Email))
                    .WithMessage("Email is too long (max 254 chars).");

            RuleFor(r => r.Company)
                .MaximumLength(120).When(r => !string.IsNullOrWhiteSpace(r.Company))
                    .WithMessage("Company is too long (max 120 chars).");

            RuleFor(r => r.JobTitle)
                .MaximumLength(120).When(r => !string.IsNullOrWhiteSpace(r.JobTitle))
                    .WithMessage("Job Title is too long (max 120 chars).");

            RuleFor(r => r.Address)
                .MaximumLength(255).When(r => !string.IsNullOrWhiteSpace(r.Address))
                    .WithMessage("Address is too long (max 255 chars).");
        }
    }

    /// <summary>
    /// Phone normalization is digits-only — strip everything else. Matches the format already
    /// stored on WhatsAppContact.PhoneNumber rows (the contacts export prepends "+" itself
    /// at render time, see ContactsService.ExportCsvAsync).
    /// </summary>
    public static class PhoneNormalizer
    {
        public static string Normalize(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
            Span<char> buf = stackalloc char[raw.Length];
            var len = 0;
            foreach (var ch in raw)
                if (ch >= '0' && ch <= '9') buf[len++] = ch;
            return new string(buf[..len]);
        }
    }
}
