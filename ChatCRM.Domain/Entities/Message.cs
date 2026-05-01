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

    public class Message
    {
        public int Id { get; set; }

        public int ConversationId { get; set; }
        public Conversation Conversation { get; set; } = null!;

        public string Body { get; set; } = string.Empty;

        public MessageDirection Direction { get; set; }

        public MessageStatus Status { get; set; } = MessageStatus.Sent;

        /// <summary>
        /// Evolution API message ID — used to deduplicate webhook deliveries.
        /// </summary>
        public string? ExternalId { get; set; }

        public DateTime SentAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Author user ID for internal notes (null for incoming/outgoing chat messages).
        /// </summary>
        public string? AuthorUserId { get; set; }
        public User? AuthorUser { get; set; }
    }
}
