using ChatCRM.Application.Chats.DTOs;

namespace ChatCRM.Application.Interfaces
{
    public interface IChatService
    {
        Task<List<ConversationDto>> GetConversationsAsync(int? instanceId = null, string? filter = null, string? currentUserId = null, byte? channelType = null, CancellationToken cancellationToken = default);
        Task<List<MessageDto>> GetMessagesAsync(int conversationId, CancellationToken cancellationToken = default);
        Task<MessageDto> SendMessageAsync(SendMessageDto dto, CancellationToken cancellationToken = default);
        Task<MessageDto> AddNoteAsync(AddNoteDto dto, string authorUserId, CancellationToken cancellationToken = default);
        Task MarkAsReadAsync(int conversationId, CancellationToken cancellationToken = default);
        Task AssignAsync(AssignDto dto, CancellationToken cancellationToken = default);
        Task SetStatusAsync(SetStatusDto dto, CancellationToken cancellationToken = default);
        Task SetLifecycleStageAsync(SetLifecycleDto dto, CancellationToken cancellationToken = default);
        Task<ContactDetailsDto?> GetContactDetailsAsync(int conversationId, CancellationToken cancellationToken = default);
        Task<List<TeamMemberDto>> GetTeamMembersAsync(CancellationToken cancellationToken = default);
    }
}
