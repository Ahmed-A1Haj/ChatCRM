using ChatCRM.Application.Chats.DTOs;

namespace ChatCRM.Application.Interfaces
{
    public interface IChatService
    {
        Task<List<ConversationDto>> GetConversationsAsync(CancellationToken cancellationToken = default);
        Task<List<MessageDto>> GetMessagesAsync(int conversationId, CancellationToken cancellationToken = default);
        Task<MessageDto> SendMessageAsync(SendMessageDto dto, CancellationToken cancellationToken = default);
        Task MarkAsReadAsync(int conversationId, CancellationToken cancellationToken = default);
    }
}
