using ChatCRM.Domain.Entities;

namespace ChatCRM.Application.Chats.DTOs
{
    /// <summary>
    /// A single media/file attachment exchanged inside a conversation, projected from a
    /// <see cref="Domain.Entities.Message"/> that carries media. Used by the Shared Media &amp;
    /// Files gallery — no separate storage, this is a read-only view over existing messages.
    /// </summary>
    public class AttachmentDto
    {
        public int Id { get; set; }
        public int ConversationId { get; set; }

        /// <summary>Message kind — Image, Video, Audio, Document, Sticker.</summary>
        public MessageKind Kind { get; set; }

        /// <summary>Public URL under /media/... — usable directly for preview and download.</summary>
        public string? MediaUrl { get; set; }
        public string? MediaMimeType { get; set; }

        /// <summary>Original file name (documents); falls back to the URL's file name otherwise.</summary>
        public string? MediaFileName { get; set; }

        /// <summary>Optional caption that accompanied the media (the message Body).</summary>
        public string? Caption { get; set; }

        public DateTime SentAt { get; set; }

        /// <summary>Incoming (from the contact) or Outgoing (from the team).</summary>
        public MessageDirection Direction { get; set; }

        /// <summary>Resolved display name of who sent it (contact / team member / AI agent).</summary>
        public string? SenderName { get; set; }

        /// <summary>File size in bytes, read from disk when the file is present locally; null otherwise.</summary>
        public long? SizeBytes { get; set; }
    }

    /// <summary>A hyperlink found in a text message — powers the optional "Links" tab.</summary>
    public class ConversationLinkDto
    {
        public int MessageId { get; set; }
        public string Url { get; set; } = string.Empty;
        public DateTime SentAt { get; set; }
        public string? SenderName { get; set; }
    }

    /// <summary>
    /// Filter + paging query for <c>GET /dashboard/chats/{id}/attachments</c>. Mirrors the
    /// existing <c>ContactsListQuery</c> convention.
    /// </summary>
    public class AttachmentsQuery
    {
        /// <summary>One of: <c>images</c>, <c>files</c>, <c>links</c>, <c>all</c>.</summary>
        public string Type { get; set; } = "images";

        /// <summary>Free-text match on file name + caption (or the URL/body for links).</summary>
        public string? Search { get; set; }

        /// <summary>Inclusive lower bound on SentAt (UTC).</summary>
        public DateTime? FromUtc { get; set; }

        /// <summary>Inclusive upper bound on SentAt (UTC).</summary>
        public DateTime? ToUtc { get; set; }

        /// <summary>Filter by sender — "incoming" (the contact) or "outgoing" (the team).</summary>
        public string? Sender { get; set; }

        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 24;
    }

    /// <summary>Paged attachment result — same shape as <c>ContactsListResultDto</c>.</summary>
    public class AttachmentsResultDto
    {
        public List<AttachmentDto> Items { get; set; } = new();

        /// <summary>Links populated only when the query Type is <c>links</c>.</summary>
        public List<ConversationLinkDto> Links { get; set; } = new();

        public int Total { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
    }

    /// <summary>
    /// View model for the Shared Media &amp; Files page header — contact identity, a way back to
    /// the conversation, and the at-a-glance counts shown on each tab.
    /// </summary>
    public class MediaGalleryViewModel
    {
        public int ConversationId { get; set; }
        public int InstanceId { get; set; }
        public string? ContactName { get; set; }
        public string? ContactPhone { get; set; }
        public string? AvatarUrl { get; set; }

        public int ImageCount { get; set; }
        public int FileCount { get; set; }
        public int LinkCount { get; set; }
    }
}
