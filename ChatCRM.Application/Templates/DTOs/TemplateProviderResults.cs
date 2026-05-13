using ChatCRM.Domain.Entities;

namespace ChatCRM.Application.Templates.DTOs
{
    /// <summary>
    /// Outcome of a Submit call to the template provider. Meta usually answers synchronously
    /// with the assigned template id and a status of <c>PENDING</c>; rejections that fall out
    /// during validation come back as <see cref="MappedStatus"/> = <c>Rejected</c> with a
    /// <see cref="FailureReason"/>. Transient transport errors are reported via thrown
    /// <see cref="HttpRequestException"/> instead of a result.
    /// </summary>
    public sealed record TemplateProviderSubmitResult
    {
        public required bool Success { get; init; }

        /// <summary>Meta's id for the template (e.g. "1234567890123"). Null on failure.</summary>
        public string? MetaTemplateId { get; init; }

        /// <summary>Mapped status — what we should record locally. Null on failure.</summary>
        public WhatsAppTemplateStatus? MappedStatus { get; init; }

        /// <summary>The exact status string Meta returned (PENDING / APPROVED / REJECTED / …).</summary>
        public string? RawStatus { get; init; }

        /// <summary>Populated when Meta rejects synchronously; null otherwise.</summary>
        public string? FailureReason { get; init; }
    }

    /// <summary>Single-template status read from Meta during a poll.</summary>
    public sealed record TemplateProviderStatus(
        WhatsAppTemplateStatus MappedStatus,
        string RawStatus,
        string? Reason,
        string? MetaTemplateId);

    /// <summary>
    /// Row in the "Sync from Meta" reconciliation. The provider returns a snapshot of every
    /// template visible to the configured WABA so the application can:
    /// (a) update local rows whose status drifted, (b) import rows that exist in Meta but not
    /// locally, (c) flag local rows that vanished from Meta.
    /// </summary>
    public sealed record TemplateProviderRemoteRow(
        string Name,
        string LanguageCode,
        WhatsAppTemplateStatus MappedStatus,
        string RawStatus,
        string? MetaTemplateId,
        string? Reason,
        MetaMessageCategory? Category,
        string? Body,
        string? Footer);
}
