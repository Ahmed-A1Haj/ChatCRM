namespace ChatCRM.Application.Templates.Exceptions
{
    /// <summary>
    /// Thrown when the configured Meta access token is missing, invalid, or has been revoked
    /// (HTTP 401 / Graph API error code 190). The application service catches this, marks the
    /// affected workspace as "Authentication required", and notifies platform admins. Submission
    /// retries are pointless until the token is rotated.
    /// </summary>
    public sealed class TemplateAuthenticationException : Exception
    {
        /// <summary>Graph API error code, when surfaced (e.g. 190 / 200).</summary>
        public int? ErrorCode { get; }

        public TemplateAuthenticationException(string message, int? errorCode = null)
            : base(message)
        {
            ErrorCode = errorCode;
        }
    }

    /// <summary>
    /// Thrown when the provider is asked to act but no Meta WABA configuration is wired up
    /// (env vars / appsettings empty). Distinct from auth failures — this is a deployment
    /// state, not a runtime-revoked credential.
    /// </summary>
    public sealed class TemplateProviderNotConfiguredException : Exception
    {
        public TemplateProviderNotConfiguredException()
            : base("Meta WABA configuration is missing — set Meta:Graph:WabaId and Meta:Graph:AccessToken before submitting templates.")
        {
        }
    }

    /// <summary>
    /// Thrown by the body validator when template content fails Meta's authoring rules
    /// (variable numbering / leading-or-trailing variables / footer variables / length).
    /// Field-level details live on <see cref="Errors"/> so the UI can highlight the right
    /// input.
    /// </summary>
    public sealed class TemplateValidationException : Exception
    {
        /// <summary>Map of field-name → error key (e.g. <c>"Body" → "Templates.Validation.LeadingVariable"</c>).</summary>
        public IReadOnlyDictionary<string, string> Errors { get; }

        public TemplateValidationException(IReadOnlyDictionary<string, string> errors)
            : base($"Template content failed validation ({errors.Count} issue(s)).")
        {
            Errors = errors;
        }
    }
}
