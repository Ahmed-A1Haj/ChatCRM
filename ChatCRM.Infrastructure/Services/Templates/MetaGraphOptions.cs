namespace ChatCRM.Infrastructure.Services.Templates
{
    /// <summary>
    /// Configuration block for the direct-Meta-Graph implementation of
    /// <see cref="ChatCRM.Application.Interfaces.IWhatsAppTemplateProvider"/>. Bound from the
    /// <c>Meta:Graph</c> section of appsettings (or env-vars in production). Secrets must come
    /// from env / user-secrets — never check <c>AccessToken</c> into appsettings.json.
    /// </summary>
    public sealed class MetaGraphOptions
    {
        /// <summary>
        /// Meta WABA (WhatsApp Business Account) id. Found in Business Manager →
        /// WhatsApp Manager → API setup. Numeric string, e.g. "1234567890123".
        /// </summary>
        public string? WabaId { get; set; }

        /// <summary>
        /// System-user or admin access token. v1 expects long-lived (system-user) tokens —
        /// admin tokens technically work but expire ~60 days and require manual rotation.
        /// </summary>
        public string? AccessToken { get; set; }

        /// <summary>Graph API host. Defaults to the public production endpoint.</summary>
        public string ApiBaseUrl { get; set; } = "https://graph.facebook.com";

        /// <summary>Graph API version segment. Bump when Meta deprecates the current one.</summary>
        public string ApiVersion { get; set; } = "v23.0";

        /// <summary>HTTP request timeout in seconds. Meta is usually fast (&lt;2s).</summary>
        public int HttpTimeoutSeconds { get; set; } = 15;
    }
}
