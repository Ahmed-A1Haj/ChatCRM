namespace ChatCRM.Domain.Entities
{
    public class Conversation
    {
        public int Id { get; set; }

        public int ContactId { get; set; }
        public WhatsAppContact Contact { get; set; } = null!;

        public string? AssignedUserId { get; set; }
        public User? AssignedUser { get; set; }

        public DateTime LastMessageAt { get; set; } = DateTime.UtcNow;

        public int UnreadCount { get; set; } = 0;

        public bool IsArchived { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<Message> Messages { get; set; } = new List<Message>();
    }
}
