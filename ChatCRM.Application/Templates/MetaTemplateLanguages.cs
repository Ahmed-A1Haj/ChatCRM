namespace ChatCRM.Application.Templates
{
    /// <summary>
    /// Curated list of WhatsApp-supported language tags exposed in the template builder. This
    /// is intentionally a *short* list of the languages this product targets — the full Meta
    /// catalogue has 70+ entries, almost all of which we don't ship. Adding more is a code
    /// change, not a runtime concern; we'd rather one more PR than 50 typo'd template
    /// rejections from a free-text input.
    ///
    /// Tags use Meta's exact format: lowercase ISO-639 + optional <c>_REGION</c>. <c>en</c> is
    /// the catch-all English; <c>en_US</c> is American English specifically — Meta treats them
    /// as different templates so we surface both.
    /// </summary>
    public static class MetaTemplateLanguages
    {
        public sealed record Entry(string Code, string EnglishName, string NativeName);

        public static readonly IReadOnlyList<Entry> All = new[]
        {
            new Entry("en",    "English",            "English"),
            new Entry("en_US", "English (US)",       "English (US)"),
            new Entry("en_GB", "English (UK)",       "English (UK)"),
            new Entry("ar",    "Arabic",             "العربية"),
            new Entry("ru",    "Russian",            "Русский"),
            new Entry("ro",    "Romanian",           "Română"),
            new Entry("tr",    "Turkish",            "Türkçe"),
            new Entry("es",    "Spanish",            "Español"),
            new Entry("es_MX", "Spanish (Mexico)",   "Español (México)"),
            new Entry("pt_BR", "Portuguese (BR)",    "Português (Brasil)"),
            new Entry("pt_PT", "Portuguese (PT)",    "Português (Portugal)"),
            new Entry("fr",    "French",             "Français"),
            new Entry("de",    "German",             "Deutsch"),
            new Entry("it",    "Italian",            "Italiano"),
            new Entry("nl",    "Dutch",              "Nederlands"),
            new Entry("pl",    "Polish",             "Polski"),
            new Entry("uk",    "Ukrainian",          "Українська"),
            new Entry("he",    "Hebrew",             "עברית"),
            new Entry("hi",    "Hindi",              "हिन्दी"),
            new Entry("id",    "Indonesian",         "Bahasa Indonesia"),
            new Entry("zh_CN", "Chinese (Simplified)", "简体中文"),
            new Entry("ja",    "Japanese",           "日本語"),
            new Entry("ko",    "Korean",             "한국어")
        };

        private static readonly HashSet<string> CodeSet =
            new(All.Select(e => e.Code), StringComparer.Ordinal);

        public static bool IsSupported(string? code)
            => !string.IsNullOrWhiteSpace(code) && CodeSet.Contains(code);
    }
}
