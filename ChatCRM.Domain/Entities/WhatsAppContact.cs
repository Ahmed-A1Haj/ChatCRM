namespace ChatCRM.Domain.Entities
{
    public class WhatsAppContact
    {
        public int Id { get; set; }

        public string PhoneNumber { get; set; } = string.Empty;

        public string? DisplayName { get; set; }

        public string? AvatarUrl { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<Conversation> Conversations { get; set; } = new List<Conversation>();
    }
}
