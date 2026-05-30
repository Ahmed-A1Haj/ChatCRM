using ChatCRM.Application.Chats.DTOs;

namespace ChatCRM.Application.Contacts.DTOs
{
    /// <summary>
    /// One lead card on the Kanban pipeline board. Projected from an existing contact — the
    /// board visualizes and mutates <see cref="ChatCRM.Domain.Entities.WhatsAppContact.LifecycleStage"/>,
    /// it does not introduce any new status concept or storage.
    /// </summary>
    public class PipelineCardDto
    {
        public int Id { get; set; }
        public string? DisplayName { get; set; }
        public string PhoneNumber { get; set; } = string.Empty;
        public string? Company { get; set; }
        public string? Email { get; set; }
        public string? AvatarUrl { get; set; }

        public byte LifecycleStage { get; set; }
        public byte ChannelType { get; set; }

        /// <summary>Channel availability for the little channel icons on the card.</summary>
        public bool HasEmail { get; set; }
        public bool HasPhone { get; set; }

        public DateTime? LastMessageAt { get; set; }

        public string? AssignedUserId { get; set; }
        public string? AssignedUserName { get; set; }

        public int? PrimaryConversationId { get; set; }
        public int? PrimaryInstanceId { get; set; }

        public List<TagDto> Tags { get; set; } = new();
    }

    /// <summary>One column = one lifecycle stage, with its cards and a true total (cards may be capped).</summary>
    public class PipelineColumnDto
    {
        public byte Stage { get; set; }
        public int Total { get; set; }
        public List<PipelineCardDto> Cards { get; set; } = new();
    }

    /// <summary>The whole board — one column per lifecycle stage, in stage order.</summary>
    public class PipelineBoardDto
    {
        public List<PipelineColumnDto> Columns { get; set; } = new();
    }

    /// <summary>Filters for the board. Applied server-side; the client re-fetches (no full reload).</summary>
    public class PipelineBoardQuery
    {
        public string? Search { get; set; }

        /// <summary>"My leads" — restrict to contacts whose primary conversation is assigned to this user.</summary>
        public string? AssignedUserId { get; set; }

        /// <summary>"This week" — only contacts active in the last 7 days.</summary>
        public bool Recent { get; set; }

        /// <summary>Max cards rendered per column (the header count still reflects the true total).</summary>
        public int PerColumnLimit { get; set; } = 100;
    }
}
