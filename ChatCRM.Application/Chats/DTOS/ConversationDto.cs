namespace ChatCRM.Application.Chats.DTOs
{
    public class ConversationDto
    {
        public int Id { get; set; }
        public int InstanceId { get; set; }
        public string InstanceDisplayName { get; set; } = string.Empty;
        public byte ChannelType { get; set; }
        public string PhoneNumber { get; set; } = string.Empty;
        public string? DisplayName { get; set; }
        public string? AvatarUrl { get; set; }
        public string LastMessage { get; set; } = string.Empty;
        public DateTime LastMessageAt { get; set; }
        public int UnreadCount { get; set; }
        public bool IsArchived { get; set; }
        public string? AssignedUserId { get; set; }
        public string? AssignedUserName { get; set; }
        public byte ConversationStatus { get; set; }
        public List<TagDto> Tags { get; set; } = new();
    }

    public class TagDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Color { get; set; } = string.Empty;
    }

    public class ContactDetailsDto
    {
        public int ConversationId { get; set; }
        public int ContactId { get; set; }
        public string PhoneNumber { get; set; } = string.Empty;
        public string? DisplayName { get; set; }
        public string? AvatarUrl { get; set; }
        public DateTime ContactCreatedAt { get; set; }
        public DateTime ConversationCreatedAt { get; set; }
        public string InstanceDisplayName { get; set; } = string.Empty;
        public string? AssignedUserId { get; set; }
        public string? AssignedUserName { get; set; }
        public byte ConversationStatus { get; set; }
        public byte LifecycleStage { get; set; }
        public int MessageCount { get; set; }
        public int NoteCount { get; set; }
        public List<TagDto> Tags { get; set; } = new();
    }

    public class SetLifecycleDto
    {
        public int ConversationId { get; set; }   // resolved server-side to ContactId
        public byte Stage { get; set; }
    }

    public class AssignDto
    {
        public int ConversationId { get; set; }
        public string? UserId { get; set; }   // null = unassign
    }

    public class AddNoteDto
    {
        public int ConversationId { get; set; }
        public string Body { get; set; } = string.Empty;
    }

    public class SetStatusDto
    {
        public int ConversationId { get; set; }
        public byte Status { get; set; }
    }

    public class TeamMemberDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Email { get; set; }
    }
}
