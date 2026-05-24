using ChatCRM.Application.Chats.DTOs;
using ChatCRM.Application.Contacts.DTOs;
// ContactDetailsDto lives in Chats.DTOs alongside the other conversation DTOs because it was
// originally a conversation-shaped payload; the new GetDetailsAsync method reuses the same
// shape so the existing populateDetails JS keeps working without a parallel DTO.

namespace ChatCRM.Application.Interfaces
{
    public interface IContactsService
    {
        Task<ContactsListResultDto> ListAsync(ContactsListQuery query, CancellationToken cancellationToken = default);

        /// <summary>
        /// Loads details for a contact that may or may not have a conversation yet — covers
        /// the imported-contact case where <see cref="IChatService.GetContactDetailsAsync"/>
        /// can't help because it requires a conversation id. If the contact has at least one
        /// conversation, the most-recent one is folded into the response so the modal can still
        /// link to "Open conversation"; otherwise the conversation-shaped fields are zeroed and
        /// <c>HasConversation</c> is false.
        /// </summary>
        Task<ContactDetailsDto?> GetDetailsAsync(int contactId, CancellationToken cancellationToken = default);

        Task SetLifecycleAsync(int contactId, byte stage, CancellationToken cancellationToken = default);
        Task SetAssigneeAsync(int contactId, string? userId, CancellationToken cancellationToken = default);
        Task SetStatusAsync(int contactId, byte status, CancellationToken cancellationToken = default);
        Task SetLanguageAsync(int contactId, string? language, CancellationToken cancellationToken = default);
        Task SetBlockAsync(int contactId, bool blocked, CancellationToken cancellationToken = default);
        Task DeleteAsync(int contactId, CancellationToken cancellationToken = default);
        Task<byte[]> ExportCsvAsync(ContactsListQuery query, CancellationToken cancellationToken = default);
    }
}
