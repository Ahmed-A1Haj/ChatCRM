using System.ComponentModel.DataAnnotations;
using ChatCRM.Domain.Entities;

namespace ChatCRM.Application.Templates.DTOs
{
    /// <summary>Compact view of an Approved template for the chat composer's picker.</summary>
    public sealed class TemplatePickerItemDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public MetaMessageCategory Category { get; set; }
        public string LanguageCode { get; set; } = "en_US";
        public string Body { get; set; } = string.Empty;
        public string? Footer { get; set; }
        public int VariableCount { get; set; }
    }

    /// <summary>Compact row for the Templates list page (active or archived tab).</summary>
    public sealed class TemplateListItemDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public MetaMessageCategory Category { get; set; }
        public string LanguageCode { get; set; } = "en_US";
        public WhatsAppTemplateStatus Status { get; set; }
        public string? RejectionReason { get; set; }
        public DateTime? SubmittedAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public string? CreatedByName { get; set; }
    }

    /// <summary>Full template payload for the editor / detail panel.</summary>
    public sealed class TemplateDetailDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public MetaMessageCategory Category { get; set; }
        public string LanguageCode { get; set; } = "en_US";
        public string Body { get; set; } = string.Empty;
        public string? Footer { get; set; }
        public WhatsAppTemplateStatus Status { get; set; }
        public string? MetaTemplateId { get; set; }
        public string? RejectionReason { get; set; }
        public int? SubmittedViaInstanceId { get; set; }
        public string? SubmittedViaInstanceName { get; set; }
        public DateTime? SubmittedAt { get; set; }
        public DateTime? StatusCheckedAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    /// <summary>Inbound payload for "save as draft / save & submit". Validated server-side.</summary>
    public class CreateTemplateDto
    {
        [Required, MaxLength(512)]
        public string Name { get; set; } = string.Empty;

        public byte Category { get; set; } = (byte)MetaMessageCategory.Utility;

        [Required, MaxLength(20)]
        public string LanguageCode { get; set; } = "en_US";

        [Required, MaxLength(1024)]
        public string Body { get; set; } = string.Empty;

        [MaxLength(60)]
        public string? Footer { get; set; }
    }

    public sealed class UpdateTemplateDto : CreateTemplateDto { }

    /// <summary>Outcome of a "Sync from Meta" reconciliation.</summary>
    public sealed class TemplateSyncResult
    {
        public int RemoteCount { get; set; }
        public int LocalUpdated { get; set; }
        public int LocalImported { get; set; }
        public int LocalOrphaned { get; set; }
        public List<string> Notes { get; set; } = new();
    }

    /// <summary>
    /// Reflects the current state of the Meta provider — used by the UI's preflight check
    /// ("Connect a WhatsApp Business account…" empty state) and to surface auth failures
    /// without forcing the user to attempt a submit first.
    /// </summary>
    public sealed class TemplateProviderStateDto
    {
        public bool IsConfigured { get; set; }
        public bool HasAuthFailure { get; set; }
        public DateTime? LastAuthFailureAtUtc { get; set; }
        public string? LastAuthFailureMessage { get; set; }
        public bool HasBusinessInstance { get; set; }
        public int SubmissionsLastHour { get; set; }
        public int SubmissionsLimitPerHour { get; set; }
    }
}
