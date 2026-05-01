using ChatCRM.Domain.Entities;

namespace ChatCRM.Application.Chats.DTOs
{
    public class InstanceDto
    {
        public int Id { get; set; }
        public string InstanceName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string? PhoneNumber { get; set; }
        public InstanceStatus Status { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? LastConnectedAt { get; set; }
        public int ConversationCount { get; set; }
        public int UnreadCount { get; set; }
    }

    public class CreateInstanceDto
    {
        public string DisplayName { get; set; } = string.Empty;
    }

    public class InstanceQrDto
    {
        public int Id { get; set; }
        public string InstanceName { get; set; } = string.Empty;
        public string? QrBase64 { get; set; }
        public InstanceStatus Status { get; set; }
    }
}
