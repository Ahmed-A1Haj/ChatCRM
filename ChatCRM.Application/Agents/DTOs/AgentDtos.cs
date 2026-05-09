using System.ComponentModel.DataAnnotations;

namespace ChatCRM.Application.Agents.DTOs
{
    /// <summary>Card-row payload for the Agents Management page.</summary>
    public class AgentListItemDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? AvatarPath { get; set; }
        public bool IsDefault { get; set; }
        public bool IsActive { get; set; }
        public string? CreatedByName { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        /// <summary>Conversations currently routed to this agent.</summary>
        public int AssignedConversationsCount { get; set; }
        /// <summary>Distinct contacts behind those conversations — surfaced as a stat tile on the card.</summary>
        public int AssignedContactsCount { get; set; }
    }

    /// <summary>Detail payload — currently identical to the list item; reserved for future fields.</summary>
    public sealed class AgentDetailDto : AgentListItemDto { }

    /// <summary>Inbound payload for create + update. Avatar is uploaded separately.</summary>
    public sealed class AgentUpsertDto
    {
        [Required, MaxLength(80)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? Description { get; set; }

        public bool IsActive { get; set; } = true;

        /// <summary>
        /// Workspace-default flag. On create: setting true promotes the new agent and demotes
        /// the previous default atomically. On update: setting true promotes (demoting any
        /// other default), setting false demotes (workspace ends up with no default until the
        /// user picks another). Both flips honour the "default must be active" invariant.
        /// </summary>
        public bool MakeDefault { get; set; }
    }

    /// <summary>Compact picker entry for the chat panel's "assign agent" dropdown.</summary>
    public sealed class AgentPickerItemDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? AvatarPath { get; set; }
        public bool IsDefault { get; set; }
    }
}
