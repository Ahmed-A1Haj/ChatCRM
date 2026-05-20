namespace ChatCRM.Domain.Entities
{
    /// <summary>
    /// Lifecycle of a WhatsApp message template, mirroring Meta's status field with two extra
    /// terminal states of our own.
    /// </summary>
    public enum WhatsAppTemplateStatus : byte
    {
        /// <summary>Saved locally, never sent to Meta. User can keep editing.</summary>
        Draft     = 0,

        /// <summary>POSTed to Meta; awaiting their decision (most resolve in &lt;1 hour).</summary>
        Submitted = 1,

        /// <summary>Meta approved — usable for outgoing sends.</summary>
        Approved  = 2,

        /// <summary>Meta rejected — see <see cref="WhatsAppTemplate.RejectionReason"/>.</summary>
        Rejected  = 3,

        /// <summary>
        /// Meta paused the template (high block rate / policy violation). Not usable until
        /// it returns to Approved.
        /// </summary>
        Paused    = 4,

        /// <summary>
        /// Soft-deleted in our app and Meta. Row is kept for audit and to block name reuse
        /// for the 30-day cool-down Meta enforces.
        /// </summary>
        Disabled  = 5,

        /// <summary>
        /// Adaptive poller gave up after 7 days in Submitted with no resolution. Manual
        /// "Sync from Meta" can still rescue this row.
        /// </summary>
        Stuck     = 6
    }

    /// <summary>
    /// A WhatsApp message template authored in our app and (eventually) registered with Meta.
    ///
    /// Workspace-wide library: templates are shared across instances within a workspace because
    /// they live in Meta's WABA, not on a single instance. <see cref="SubmittedViaInstanceId"/>
    /// captures which Business instance was used at submission time for audit purposes.
    ///
    /// Body supports <c>{{1}}</c>, <c>{{2}}</c>, …-style placeholders. Footer is optional, plain
    /// text only, max 60 chars (Meta limit). Variables must be sequential from {{1}} with no gaps
    /// and may not appear at the very start or end of the body — Meta rejects those.
    /// </summary>
    public class WhatsAppTemplate
    {
        public int Id { get; set; }

        /// <summary>Workspace owner. Singleton (=1) for v1.</summary>
        public int WorkspaceId { get; set; }

        /// <summary>Meta-required: lowercase letters/digits/underscores, 1–512 chars.</summary>
        public string Name { get; set; } = string.Empty;

        public MetaMessageCategory Category { get; set; }

        /// <summary>
        /// IETF language tag understood by Meta (e.g. <c>en_US</c>, <c>ar</c>). Restricted at the
        /// service layer to a curated list to avoid Meta rejections from typos.
        /// </summary>
        public string LanguageCode { get; set; } = "en_US";

        /// <summary>Body text with {{n}} placeholders. Max 1024 chars per Meta spec.</summary>
        public string Body { get; set; } = string.Empty;

        /// <summary>
        /// Optional footer (max 60 chars, plain text, no variables). Required by Meta for
        /// legitimate marketing templates — legal disclaimer / opt-out instructions live here.
        /// </summary>
        public string? Footer { get; set; }

        public WhatsAppTemplateStatus Status { get; set; } = WhatsAppTemplateStatus.Draft;

        /// <summary>
        /// Meta's id for this template (returned on successful submission). Null until the
        /// submission round-trip completes.
        /// </summary>
        public string? MetaTemplateId { get; set; }

        /// <summary>Populated when Meta rejects or pauses; null otherwise.</summary>
        public string? RejectionReason { get; set; }

        /// <summary>
        /// The Business instance whose WABA we used at submission. Audit-only — once a template
        /// is approved, all instances in the same WABA can use it.
        /// </summary>
        public int? SubmittedViaInstanceId { get; set; }
        public WhatsAppInstance? SubmittedViaInstance { get; set; }

        public DateTime? SubmittedAt { get; set; }

        /// <summary>
        /// Last time the adaptive poller checked Meta for this template's status. Drives the
        /// next-tick decision (see <c>TemplateStatusSyncService</c> in phase 6 part 2).
        /// </summary>
        public DateTime? StatusCheckedAt { get; set; }

        public string? CreatedByUserId { get; set; }
        public User? CreatedByUser { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
