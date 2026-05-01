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

        public DateTime LastMessageAt { get; set; } = DateTime.UtcNow;

        public int UnreadCount { get; set; } = 0;

        public bool IsArchived { get; set; } = false;

        public ConversationStatus Status { get; set; } = ConversationStatus.Open;

        public DateTime? SnoozedUntil { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<Message> Messages { get; set; } = new List<Message>();
        public ICollection<ConversationTag> Tags { get; set; } = new List<ConversationTag>();
    }
}
