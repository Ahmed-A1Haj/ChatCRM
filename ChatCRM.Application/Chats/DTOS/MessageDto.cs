using ChatCRM.Domain.Entities;

namespace ChatCRM.Application.Chats.DTOs
{
    public class MessageDto
    {
        public int Id { get; set; }
        public string Body { get; set; } = string.Empty;
        public MessageDirection Direction { get; set; }
        public MessageStatus Status { get; set; }
        public DateTime SentAt { get; set; }
        public string? AuthorName { get; set; }

        public MessageKind Kind { get; set; } = MessageKind.Text;
        public string? MediaUrl { get; set; }
        public string? MediaMimeType { get; set; }
        public string? MediaFileName { get; set; }

        public DateTime? EditedAt { get; set; }
        public bool IsDeleted { get; set; }
    }
}
