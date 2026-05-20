using System.ComponentModel.DataAnnotations;

namespace ChatCRM.Application.Chats.DTOs
{
    public class SendMessageDto
    {
        [Required]
        public int ConversationId { get; set; }

        [Required]
        [MaxLength(4096)]
        public string Body { get; set; } = string.Empty;
    }
}
