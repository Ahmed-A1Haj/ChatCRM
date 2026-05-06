using ChatCRM.Application.Templates.DTOs;

namespace ChatCRM.Application.Interfaces
{
    /// <summary>
    /// Application-layer orchestration for the WhatsApp template manager. CRUD over the
    /// workspace-wide template library plus submission, soft-delete, and the manual
    /// "Sync from Meta" reconciliation. Wraps <see cref="IWhatsAppTemplateProvider"/> with
    /// rate limiting, validation, audit logging, and persistence.
    /// </summary>
    public interface ITemplateService
    {
        /// <summary>List templates for the active or archived tab. Active = anything except Disabled.</summary>
        Task<List<TemplateListItemDto>> ListAsync(bool archived, CancellationToken cancellationToken = default);

        Task<TemplateDetailDto?> GetAsync(int id, CancellationToken cancellationToken = default);

        /// <summary>
        /// Persist a new template as Draft. Validates content + uniqueness. Throws
        /// <see cref="ChatCRM.Application.Templates.Exceptions.TemplateValidationException"/>
        /// on bad content or duplicate (Name, LanguageCode).
        /// </summary>
        Task<TemplateDetailDto> CreateDraftAsync(
            CreateTemplateDto dto, string actingUserId, CancellationToken cancellationToken = default);

        /// <summary>Update a draft. Approved/Submitted templates are immutable in v1 (Meta forbids edits without re-submission).</summary>
        Task<TemplateDetailDto> UpdateDraftAsync(
            int id, UpdateTemplateDto dto, string actingUserId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Submit the template to Meta. Enforces the workspace submission rate limit before
        /// the network call. Rejected templates can be re-submitted after edit; Approved
        /// templates can't be re-submitted (Meta would reject as duplicate).
        /// </summary>
        Task<TemplateDetailDto> SubmitAsync(
            int id, int? submittedViaInstanceId, string actingUserId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Soft-delete: call Meta delete (for non-Draft templates), mark our row Disabled,
        /// keep the row for audit + 30-day name reservation. Drafts skip the Meta call.
        /// </summary>
        Task SoftDeleteAsync(int id, string actingUserId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Manual reconciliation triggered by the "Sync from Meta" button. Pulls the WABA's
        /// full template list and updates / imports / orphans local rows. Imports come in as
        /// non-editable (Status reflects Meta's view).
        /// </summary>
        Task<TemplateSyncResult> SyncFromMetaAsync(
            string actingUserId, CancellationToken cancellationToken = default);

        /// <summary>UI preflight + diagnostic state — drives the empty state on the Templates page.</summary>
        Task<TemplateProviderStateDto> GetProviderStateAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Compact list of Approved templates with body + variable count — feeds the chat
        /// composer's "Pick template" dropdown. Drafts/rejected/etc. are filtered out.
        /// </summary>
        Task<List<TemplatePickerItemDto>> ListApprovedForPickerAsync(CancellationToken cancellationToken = default);
    }
}
