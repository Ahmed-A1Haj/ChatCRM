namespace ChatCRM.Application.Contacts.DTOs
{
    public class ContactRowDto
    {
        public int Id { get; set; }
        public string PhoneNumber { get; set; } = string.Empty;
        public string? DisplayName { get; set; }
        public string Channel { get; set; } = string.Empty;        // e.g. "WhatsApp"
        public byte ChannelType { get; set; }
        public DateTime? LastMessageAt { get; set; }
        public string? LastMessagePreview { get; set; }
        public DateTime CreatedAt { get; set; }
        public string? Country { get; set; }
        public string? Language { get; set; }
        public byte LifecycleStage { get; set; }
        public string? AssignedUserId { get; set; }
        public string? AssignedUserName { get; set; }
        public byte? ConversationStatus { get; set; }
        public bool IsBlocked { get; set; }
        public int? PrimaryConversationId { get; set; }
        public int? PrimaryInstanceId { get; set; }
    }

    public class ContactsListResultDto
    {
        public List<ContactRowDto> Items { get; set; } = new();
        public int Total { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
    }

    public class ContactsListQuery
    {
        public string? Search { get; set; }
        public byte? Lifecycle { get; set; }
        public string? AssignedUserId { get; set; }
        public byte? Status { get; set; }            // 0 Open, 2 Closed
        public bool? Blocked { get; set; }
        public string Sort { get; set; } = "lastMessage"; // lastMessage | name | createdAt
        public string Direction { get; set; } = "desc";
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 25;
    }

    public class UpdateContactLanguageDto
    {
        public int ContactId { get; set; }
        public string? Language { get; set; }
    }

    public class UpdateContactCountryDto
    {
        public int ContactId { get; set; }
        public string? Country { get; set; }
    }

    public class SetBlockDto
    {
        public int ContactId { get; set; }
        public bool Blocked { get; set; }
    }
}
