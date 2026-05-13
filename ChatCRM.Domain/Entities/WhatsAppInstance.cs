namespace ChatCRM.Domain.Entities
{
    public enum InstanceStatus : byte
    {
        Pending = 0,
        Connecting = 1,
        Connected = 2,
        Disconnected = 3
    }

    /// <summary>
    /// How a WhatsApp account is connected. Maps to Evolution API's `integration` field:
    /// Personal == "WHATSAPP-BAILEYS" (QR-scanned WhatsApp Web account, prosumer features),
    /// Business == "WHATSAPP-BUSINESS" (official Cloud API via Meta, full features).
    /// </summary>
    public enum WhatsAppIntegration : byte
    {
        Personal = 0,
        Business = 1
    }

    public class WhatsAppInstance
    {
        public int Id { get; set; }

        public string InstanceName { get; set; } = string.Empty;

        public string DisplayName { get; set; } = string.Empty;

        public ChannelType ChannelType { get; set; } = ChannelType.WhatsApp;

        /// <summary>Personal (Baileys/WhatsApp Web) vs. Business (Cloud API). Captured at create time.</summary>
        public WhatsAppIntegration Integration { get; set; } = WhatsAppIntegration.Personal;

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
