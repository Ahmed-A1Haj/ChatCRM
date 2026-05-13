using ChatCRM.Application.Agents.DTOs;

namespace ChatCRM.Application.Interfaces
{
    public enum AgentListFilter : byte
    {
        All      = 0,
        Active   = 1,
        Inactive = 2
    }

    /// <summary>
    /// CRUD + invariant-enforcing operations for workspace AI agents. Mirrors the shape of
    /// the existing template / billing services: thin DTO surface, audit-logged mutations,
    /// throws domain-specific exceptions for business-rule violations.
    /// </summary>
    public interface IAgentService
    {
        Task<List<AgentListItemDto>> ListAsync(
            AgentListFilter filter, string? searchTerm,
            CancellationToken cancellationToken = default);

        Task<AgentDetailDto?> GetAsync(int id, CancellationToken cancellationToken = default);

        Task<AgentDetailDto> CreateAsync(
            AgentUpsertDto dto, string actingUserId,
            CancellationToken cancellationToken = default);

        Task<AgentDetailDto> UpdateAsync(
            int id, AgentUpsertDto dto, string actingUserId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Hard-delete an agent (no soft-delete here — agents are workspace config, not
        /// audit-relevant historical state). Throws if the target is the current default;
        /// caller must promote another agent first.
        /// </summary>
        Task DeleteAsync(int id, string actingUserId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Promote the agent to workspace default. Atomically: clears the previous default's
        /// <c>IsDefault</c> bit, sets the target's bit, persists. The target must be active —
        /// activate it first if needed.
        /// </summary>
        Task<AgentDetailDto> SetDefaultAsync(int id, string actingUserId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Toggle the agent's active state. Refuses to deactivate the default; activating an
        /// inactive agent is always allowed.
        /// </summary>
        Task<AgentDetailDto> SetActiveAsync(int id, bool isActive, string actingUserId, CancellationToken cancellationToken = default);

        /// <summary>Set the avatar path after a successful upload.</summary>
        Task<AgentDetailDto> SetAvatarAsync(int id, string avatarPath, string actingUserId, CancellationToken cancellationToken = default);

        /// <summary>Picker entries for the chat panel — only active agents.</summary>
        Task<List<AgentPickerItemDto>> ListActiveForPickerAsync(CancellationToken cancellationToken = default);

        /// <summary>The id of the workspace's default agent, or null if none configured.</summary>
        Task<int?> GetDefaultAgentIdAsync(CancellationToken cancellationToken = default);

        /// <summary>Reassign a conversation to an agent. Pass null to unassign.</summary>
        Task ReassignConversationAsync(
            int conversationId, int? agentId, string actingUserId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Set the per-contact agent preference. Pass null to clear (falls back to workspace
        /// default at next conversation auto-creation). Throws if the target agent is inactive.
        /// </summary>
        Task<ContactAgentAssignmentDto> AssignContactAsync(
            int contactId, int? agentId, string actingUserId,
            CancellationToken cancellationToken = default);
    }
}
