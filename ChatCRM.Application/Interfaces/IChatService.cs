using ChatCRM.Application.Chats.DTOs;

namespace ChatCRM.Application.Interfaces
{
    public interface IChatService
    {
        Task<List<ConversationDto>> GetConversationsAsync(int? instanceId = null, string? filter = null, string? currentUserId = null, byte? channelType = null, CancellationToken cancellationToken = default);
        Task<List<MessageDto>> GetMessagesAsync(int conversationId, CancellationToken cancellationToken = default);
        Task<MessageDto> SendMessageAsync(SendMessageDto dto, string actingUserId, CancellationToken cancellationToken = default);
        Task<MessageDto> AddNoteAsync(AddNoteDto dto, string authorUserId, CancellationToken cancellationToken = default);
        Task MarkAsReadAsync(int conversationId, CancellationToken cancellationToken = default);
        Task AssignAsync(AssignDto dto, CancellationToken cancellationToken = default);
        Task SetStatusAsync(SetStatusDto dto, CancellationToken cancellationToken = default);
        Task SetLifecycleStageAsync(SetLifecycleDto dto, CancellationToken cancellationToken = default);
        Task<ContactDetailsDto?> GetContactDetailsAsync(int conversationId, CancellationToken cancellationToken = default);
        Task<List<TeamMemberDto>> GetTeamMembersAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Header info + per-tab counts for the Shared Media &amp; Files gallery. Returns null when
        /// the conversation doesn't exist.
        /// </summary>
        Task<MediaGalleryViewModel?> GetMediaGalleryAsync(int conversationId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Paginated attachments (images / files / links) for one conversation, projected from the
        /// messages already stored — no duplicate storage. Newest first.
        /// </summary>
        Task<AttachmentsResultDto> GetAttachmentsAsync(int conversationId, AttachmentsQuery query, CancellationToken cancellationToken = default);

        Task<MessageDto> EditMessageAsync(int messageId, string newBody, CancellationToken cancellationToken = default);
        Task DeleteMessageAsync(int messageId, CancellationToken cancellationToken = default);

        Task<MessageDto> SendMediaMessageAsync(int conversationId, byte[] data, string fileName, string mimeType, string? caption, string actingUserId, CancellationToken cancellationToken = default);
        Task<MessageDto> SendVoiceNoteAsync(int conversationId, byte[] data, string mimeType, string actingUserId, CancellationToken cancellationToken = default);

        /// <summary>Read-only transcription for one audio message. Null if the message doesn't exist.</summary>
        Task<ChatCRM.Application.Transcription.MessageTranscriptionDto?> GetTranscriptionAsync(int messageId, CancellationToken cancellationToken = default);

        /// <summary>Re-queues a message for transcription (resets attempts). Returns false if not an audio message.</summary>
        Task<bool> RetryTranscriptionAsync(int messageId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Send an approved WhatsApp template. <paramref name="variables"/> are the values for
        /// {{1}}, {{2}}, … in template order. Bills against the template's category, not Service.
        /// </summary>
        Task<MessageDto> SendTemplateMessageAsync(
            int conversationId, int templateId,
            IReadOnlyList<string> variables, string actingUserId,
            CancellationToken cancellationToken = default);
    }
}
