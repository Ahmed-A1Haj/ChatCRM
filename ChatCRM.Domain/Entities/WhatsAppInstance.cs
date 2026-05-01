namespace ChatCRM.Domain.Entities
{
    public enum InstanceStatus : byte
    {
        Pending = 0,
        Connecting = 1,
        Connected = 2,
        Disconnected = 3
    }

    public class WhatsAppInstance
    {
        public int Id { get; set; }

        public string InstanceName { get; set; } = string.Empty;

        public string DisplayName { get; set; } = string.Empty;

        public ChannelType ChannelType { get; set; } = ChannelType.WhatsApp;

        public string? PhoneNumber { get; set; }

        public string? OwnerJid { get; set; }

        public InstanceStatus Status { get; set; } = InstanceStatus.Pending;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? LastConnectedAt { get; set; }

        public string? CreatedByUserId { get; set; }
        public User? CreatedByUser { get; set; }

        public ICollection<Conversation> Conversations { get; set; } = new List<Conversation>();
    }
}
