namespace ChatCRM.Domain.Entities
{
    /// <summary>
    /// Workspace-scoped AI agent. Each workspace can author many agents (one of which is
    /// the <see cref="IsDefault"/> for auto-assignment of new conversations) and toggle
    /// any of them <see cref="IsActive"/> independently.
    ///
    /// Invariants enforced at the service layer:
    ///   • Exactly one agent per workspace has IsDefault=true (DB enforces via a filtered
    ///     unique index — see AppDbContext config).
    ///   • The default agent must always be active (deactivating the default is rejected;
    ///     promoting an inactive agent to default is rejected).
    ///   • The default agent cannot be deleted — caller must promote a successor first.
    ///
    /// Conversations are auto-assigned to the workspace's default agent when first created
    /// (see <see cref="Conversation.AssignedAgentId"/>). Manual reassignment is allowed
    /// from the chat panel.
    /// </summary>
    public class Agent
    {
        public int Id { get; set; }

        /// <summary>Workspace owner. Singleton (=1) for v1.</summary>
        public int WorkspaceId { get; set; }

        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }

        /// <summary>Web-served path under <c>/avatars/agents/...</c>. Null = use the default avatar.</summary>
        public string? AvatarPath { get; set; }

        public bool IsDefault { get; set; }
        public bool IsActive { get; set; } = true;

        public string? CreatedByUserId { get; set; }
        public User? CreatedByUser { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
