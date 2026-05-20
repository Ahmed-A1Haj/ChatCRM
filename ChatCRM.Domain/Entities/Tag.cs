namespace ChatCRM.Domain.Entities
{
    public class Tag
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        /// <summary>Hex color (e.g. "#25d366") used to render the tag chip.</summary>
        public string Color { get; set; } = "#64748b";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<ConversationTag> ConversationTags { get; set; } = new List<ConversationTag>();
    }

    /// <summary>Join entity for the many-to-many between Conversations and Tags.</summary>
    public class ConversationTag
    {
        public int ConversationId { get; set; }
        public Conversation Conversation { get; set; } = null!;

        public int TagId { get; set; }
        public Tag Tag { get; set; } = null!;
    }
}
