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
    }
}
