namespace ChatCRM.Domain.Entities
{
    public class WhatsAppContact
    {
        public int Id { get; set; }

        public string PhoneNumber { get; set; } = string.Empty;

        /// <summary>
        /// Full Baileys/WhatsApp JID (e.g. "972595276896@s.whatsapp.net" or "...@lid").
        /// Captured from incoming webhooks and required for Evolution edit/delete API calls.
        /// </summary>
        public string? RemoteJid { get; set; }

        public string? DisplayName { get; set; }

        public string? AvatarUrl { get; set; }

        public LifecycleStage LifecycleStage { get; set; } = LifecycleStage.NewClient;

        public string? Country { get; set; }

        public string? Language { get; set; }

        public bool IsBlocked { get; set; } = false;

        /// <summary>
        /// Per-contact AI agent preference. When a new <see cref="Conversation"/> is created
        /// for this contact, the auto-assignment in <c>EvolutionService.HandleIncomingWebhookAsync</c>
        /// uses this id first (if active), falling back to the workspace default agent. Null
        /// means "no preference — use workspace default".
        /// </summary>
        public int? AssignedAgentId { get; set; }
        public Agent? AssignedAgent { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<Conversation> Conversations { get; set; } = new List<Conversation>();
    }
}
