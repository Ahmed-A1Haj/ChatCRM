namespace ChatCRM.Application.Contacts.Import
{
    /// <summary>
    /// Canonical header names for the import template. Held in one place so the parser, the
    /// template generator, the error report, and the validator messages all stay in sync.
    /// </summary>
    public static class ExcelColumns
    {
        public const string FirstName   = "First Name";
        public const string LastName    = "Last Name";
        public const string PhoneNumber = "Phone Number";
        public const string Email       = "Email";
        public const string Company     = "Company";
        public const string JobTitle    = "Job Title";
        public const string Address     = "Address";
        public const string Notes       = "Notes";

        /// <summary>
        /// Header order used when writing the empty template and the error report.
        /// </summary>
        public static readonly string[] All =
        {
            FirstName, LastName, PhoneNumber, Email, Company, JobTitle, Address, Notes
        };

        /// <summary>
        /// Only Phone Number is required — it's the unique key on WhatsAppContacts.
        /// </summary>
        public static readonly HashSet<string> Required = new(StringComparer.OrdinalIgnoreCase)
        {
            PhoneNumber
        };

        /// <summary>
        /// Case- and whitespace-insensitive header matching — "phone_number", "Phone Number",
        /// "  PHONE NUMBER  " all collapse to the same key.
        /// </summary>
        public static string Canonicalize(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
            Span<char> buf = stackalloc char[raw.Length];
            var len = 0;
            foreach (var ch in raw)
            {
                if (char.IsLetterOrDigit(ch))
                    buf[len++] = char.ToLowerInvariant(ch);
            }
            return new string(buf[..len]);
        }
    }
}
