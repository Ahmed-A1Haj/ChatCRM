namespace ChatCRM.Domain.Entities
{
    public enum MessageDirection : byte
    {
        Incoming = 0,
        Outgoing = 1,
        /// <summary>Internal team note — not sent to WhatsApp, visible only to agents.</summary>
        Note = 2
    }

    public enum MessageStatus : byte
    {
        Sent = 0,
        Delivered = 1,
        Read = 2
    }

    public enum MessageKind : byte
    {
        Text = 0,
        Image = 1,
        Video = 2,
        Audio = 3,
        Document = 4,
        Sticker = 5
    }

    public class Message
    {
        public int Id { get; set; }

        public int ConversationId { get; set; }
        public Conversation Conversation { get; set; } = null!;

        public string Body { get; set; } = string.Empty;

        public MessageDirection Direction { get; set; }

        public MessageStatus Status { get; set; } = MessageStatus.Sent;

        public MessageKind Kind { get; set; } = MessageKind.Text;

        /// <summary>Public URL (under /media/...) of the attached file when Kind != Text.</summary>
        public string? MediaUrl { get; set; }

        /// <summary>MIME type of the attached file, e.g. "image/jpeg".</summary>
        public string? MediaMimeType { get; set; }

        /// <summary>Original filename (only meaningful for documents).</summary>
        public string? MediaFileName { get; set; }

        /// <summary>
        /// Evolution API message ID — used to deduplicate webhook deliveries.
        /// </summary>
        public string? ExternalId { get; set; }

        public DateTime SentAt { get; set; } = DateTime.UtcNow;

        /// <summary>Set when the contact edits the message on WhatsApp; null otherwise.</summary>
        public DateTime? EditedAt { get; set; }

        /// <summary>True when the contact revokes ("delete for everyone") the message on WhatsApp.</summary>
        public bool IsDeleted { get; set; }

        /// <summary>
        /// Author user ID for internal notes (null for incoming/outgoing chat messages).
        /// </summary>
        public string? AuthorUserId { get; set; }
        public User? AuthorUser { get; set; }
    }
}
