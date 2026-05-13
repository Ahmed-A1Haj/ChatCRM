namespace ChatCRM.Domain.Entities
{
    public enum ConversationStatus : byte
    {
        Open = 0,
        Snoozed = 1,
        Closed = 2
    }

    public class Conversation
    {
        public int Id { get; set; }

        public int ContactId { get; set; }
        public WhatsAppContact Contact { get; set; } = null!;

        public int WhatsAppInstanceId { get; set; }
        public WhatsAppInstance Instance { get; set; } = null!;

        public string? AssignedUserId { get; set; }
        public User? AssignedUser { get; set; }

        /// <summary>
        /// AI agent handling this conversation. Auto-populated from the workspace's default
        /// agent at conversation creation time; agents can be reassigned manually from the
        /// chat panel. Null when no agents exist in the workspace.
        /// </summary>
        public int? AssignedAgentId { get; set; }
        public Agent? AssignedAgent { get; set; }

        public DateTime LastMessageAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Timestamp of the most recent INCOMING (contact → us) message. Drives the WhatsApp
        /// 24-hour customer-service window: while &lt; 24h ago, we can free-form-reply to
        /// Cloud-API conversations; outside the window, only approved templates are allowed.
        /// Maintained denormalized so we don't need a MAX(...) per render. Null means the
        /// contact has never sent us a message yet (we initiated the conversation).
        /// </summary>
        public DateTime? LastIncomingAt { get; set; }

        public int UnreadCount { get; set; } = 0;

        public bool IsArchived { get; set; } = false;

        public ConversationStatus Status { get; set; } = ConversationStatus.Open;

        public DateTime? SnoozedUntil { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<Message> Messages { get; set; } = new List<Message>();
        public ICollection<ConversationTag> Tags { get; set; } = new List<ConversationTag>();
    }
}
