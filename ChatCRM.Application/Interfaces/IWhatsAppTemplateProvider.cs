using ChatCRM.Application.Templates.DTOs;
using ChatCRM.Domain.Entities;

namespace ChatCRM.Application.Interfaces
{
    /// <summary>
    /// Abstraction over the Meta-side template registry. v1 implementation talks directly to
    /// Meta's Graph API at <c>/{waba_id}/message_templates</c>. The interface is shaped to also
    /// fit a future Evolution-API-backed provider should Evolution add template management
    /// endpoints — only the constructor wiring would change.
    ///
    /// Errors:
    ///   - <see cref="ChatCRM.Application.Templates.Exceptions.TemplateProviderNotConfiguredException"/>
    ///     when WABA id / access token are missing.
    ///   - <see cref="ChatCRM.Application.Templates.Exceptions.TemplateAuthenticationException"/>
    ///     for HTTP 401 / Graph error 190 (revoked or expired token).
    ///   - <see cref="HttpRequestException"/> for transient transport failures (caller may retry).
    /// </summary>
    public interface IWhatsAppTemplateProvider
    {
        /// <summary>
        /// True only when the provider has everything it needs to call Meta. Used by the UI's
        /// preflight check ("Connect a WhatsApp Business account…") so users can't draft
        /// templates they can't submit.
        /// </summary>
        bool IsConfigured { get; }

        /// <summary>
        /// POST a new template to Meta. Body and footer are passed as-is — caller is responsible
        /// for running the validator first. <paramref name="name"/> must be unique within the WABA
        /// for the given <paramref name="languageCode"/>; uniqueness is also enforced locally by
        /// a unique index on (WorkspaceId, Name, LanguageCode).
        /// </summary>
        Task<TemplateProviderSubmitResult> SubmitAsync(
            string name,
            string languageCode,
            MetaMessageCategory category,
            string body,
            string? footer,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Refresh the status of a single template by Meta id. Returns null if Meta no longer
        /// knows about the template (deleted in Business Manager).
        /// </summary>
        Task<TemplateProviderStatus?> GetStatusAsync(
            string metaTemplateId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Pull every template the configured WABA can see — feeds the manual "Sync from Meta"
        /// reconciliation. Caller is responsible for paging if the result set is huge (Meta
        /// pages at 1000 by default which is fine for v1).
        /// </summary>
        Task<IReadOnlyList<TemplateProviderRemoteRow>> ListAllAsync(
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Soft-delete the named template at Meta. Meta marks it DELETED and reserves the name
        /// for 30 days. Returns true on success; false when Meta doesn't recognise the name.
        /// </summary>
        Task<bool> DeleteAsync(
            string name,
            CancellationToken cancellationToken = default);
    }
}
