using System.Text.RegularExpressions;
using ChatCRM.Application.Templates.Exceptions;

namespace ChatCRM.Application.Templates.Validation
{
    /// <summary>
    /// Author-time validator for template content. Catches the rule violations Meta would
    /// otherwise reject for at submission time — running these locally turns a 30-second
    /// async rejection round-trip into an immediate inline form error.
    ///
    /// Rules enforced (all derived from Meta's published template authoring rules):
    ///  1. Name: lowercase letters/digits/underscores only, 1–512 chars.
    ///  2. Body: required, ≤1024 chars (we cap at 2000 in the column for safety, but Meta
    ///     rejects above 1024 for non-AUTHENTICATION categories).
    ///  3. Body variables: must be sequential from {{1}}, no gaps, no duplicates skipped.
    ///  4. Body must NOT start with a variable (Meta rejects "{{1}} hello").
    ///  5. Body must NOT end with a variable (Meta rejects "hello {{1}}").
    ///  6. Footer: optional, ≤60 chars, must NOT contain {{ ... }} variables.
    ///  7. Language: must be in the curated <see cref="MetaTemplateLanguages"/> list.
    /// </summary>
    public static class TemplateContentValidator
    {
        // Lowercase a-z, 0-9, underscore. Meta also forbids leading digits/underscore? In
        // practice the API accepts those, but we're conservative and require it to start
        // with a letter — matches Meta's recommended style.
        private static readonly Regex NameRegex =
            new("^[a-z][a-z0-9_]{0,511}$", RegexOptions.Compiled);

        // Captures every {{n}} placeholder where n is a positive integer. Whitespace inside
        // is intentionally rejected — Meta accepts {{1}} but not {{ 1 }}.
        private static readonly Regex VariableRegex =
            new(@"\{\{(\d+)\}\}", RegexOptions.Compiled);

        // Looser regex used only to detect *any* mustache-like sequence so the footer check
        // catches typos like {{1} or {1}}. Anything resembling a variable in the footer is
        // a hard fail.
        private static readonly Regex FooterVariableRegex =
            new(@"\{\{|\}\}", RegexOptions.Compiled);

        private const int BodyMaxLength   = 1024;
        private const int FooterMaxLength = 60;
        private const int NameMaxLength   = 512;

        /// <summary>
        /// Validate template content. Throws <see cref="TemplateValidationException"/> with
        /// a per-field error map on failure. The error values are translation keys so the UI
        /// can localise.
        /// </summary>
        public static void Validate(string name, string languageCode, string body, string? footer)
        {
            var errors = new Dictionary<string, string>();

            // ── Name ────────────────────────────────────────────────────────
            if (string.IsNullOrWhiteSpace(name))
            {
                errors["Name"] = "Templates.Validation.NameRequired";
            }
            else if (name.Length > NameMaxLength)
            {
                errors["Name"] = "Templates.Validation.NameTooLong";
            }
            else if (!NameRegex.IsMatch(name))
            {
                errors["Name"] = "Templates.Validation.NameFormat";
            }

            // ── Language ────────────────────────────────────────────────────
            if (!MetaTemplateLanguages.IsSupported(languageCode))
                errors["LanguageCode"] = "Templates.Validation.LanguageUnsupported";

            // ── Body ────────────────────────────────────────────────────────
            if (string.IsNullOrWhiteSpace(body))
            {
                errors["Body"] = "Templates.Validation.BodyRequired";
            }
            else if (body.Length > BodyMaxLength)
            {
                errors["Body"] = "Templates.Validation.BodyTooLong";
            }
            else
            {
                ValidateBodyVariables(body, errors);
            }

            // ── Footer ──────────────────────────────────────────────────────
            if (!string.IsNullOrWhiteSpace(footer))
            {
                if (footer.Length > FooterMaxLength)
                    errors["Footer"] = "Templates.Validation.FooterTooLong";
                else if (FooterVariableRegex.IsMatch(footer))
                    errors["Footer"] = "Templates.Validation.FooterNoVariables";
            }

            if (errors.Count > 0)
                throw new TemplateValidationException(errors);
        }

        private static void ValidateBodyVariables(string body, Dictionary<string, string> errors)
        {
            var trimmed = body.Trim();

            // Leading variable: body starts with {{n}} after trimming.
            if (trimmed.StartsWith("{{"))
            {
                errors["Body"] = "Templates.Validation.LeadingVariable";
                return;
            }

            // Trailing variable: body ends with }} after trimming.
            if (trimmed.EndsWith("}}"))
            {
                errors["Body"] = "Templates.Validation.TrailingVariable";
                return;
            }

            var matches = VariableRegex.Matches(body);
            if (matches.Count == 0) return; // no variables — that's fine

            var seen = new HashSet<int>();
            var indexes = new List<int>(matches.Count);
            foreach (Match m in matches)
            {
                if (!int.TryParse(m.Groups[1].Value, out var n) || n <= 0)
                {
                    errors["Body"] = "Templates.Validation.VariableInvalid";
                    return;
                }
                if (seen.Add(n)) indexes.Add(n);
            }

            // Sequential check: distinct values must be exactly 1, 2, …, k for some k.
            indexes.Sort();
            for (var i = 0; i < indexes.Count; i++)
            {
                if (indexes[i] != i + 1)
                {
                    errors["Body"] = "Templates.Validation.VariableNotSequential";
                    return;
                }
            }
        }

        /// <summary>
        /// Lightweight helper for the "send template" path (phase 7) — counts the highest
        /// variable index in a validated body so the composer knows how many input fields to
        /// render. Returns 0 when the body has no variables.
        /// </summary>
        public static int CountVariables(string body)
        {
            var max = 0;
            foreach (Match m in VariableRegex.Matches(body))
            {
                if (int.TryParse(m.Groups[1].Value, out var n) && n > max)
                    max = n;
            }
            return max;
        }
    }
}
