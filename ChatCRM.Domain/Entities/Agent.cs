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

        /// <summary>
        /// Free-form system-prompt / role definition for the AI agent. Stored as
        /// <c>nvarchar(max)</c> because well-tuned prompts can run into thousands of chars
        /// (think few-shot examples and tool definitions). Service layer caps at 8,000 chars
        /// for sanity — anything beyond starts hurting context-window economics anyway.
        /// </summary>
        public string? Instructions { get; set; }

        public bool IsDefault { get; set; }
        public bool IsActive { get; set; } = true;

        public string? CreatedByUserId { get; set; }
        public User? CreatedByUser { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // ── CRM-AI-Service sync ─────────────────────────────────────────────
        // The AI service stores its own agent row (Postgres + pgvector). We
        // round-trip its UUID back here so future chat-turn dispatches can
        // address the remote agent directly.

        /// <summary>UUID of this agent in the remote CRM-AI-Service. Null until first sync succeeds.</summary>
        public Guid? RemoteAgentId { get; set; }

        /// <summary><c>pending</c> | <c>synced</c> | <c>error</c>.</summary>
        public string RemoteSyncStatus { get; set; } = "pending";

        public DateTime? RemoteSyncedAt { get; set; }

        /// <summary>Last error from the remote sync, if status is <c>error</c>.</summary>
        public string? RemoteSyncError { get; set; }
    }
}
