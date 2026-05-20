using ChatCRM.Application.Chats.DTOs;
using ChatCRM.Application.Contacts.DTOs;

namespace ChatCRM.Application.Interfaces
{
    public interface IContactsService
    {
        Task<ContactsListResultDto> ListAsync(ContactsListQuery query, CancellationToken cancellationToken = default);
        Task SetLifecycleAsync(int contactId, byte stage, CancellationToken cancellationToken = default);
        Task SetAssigneeAsync(int contactId, string? userId, CancellationToken cancellationToken = default);
        Task SetStatusAsync(int contactId, byte status, CancellationToken cancellationToken = default);
        Task SetLanguageAsync(int contactId, string? language, CancellationToken cancellationToken = default);
        Task SetBlockAsync(int contactId, bool blocked, CancellationToken cancellationToken = default);
        Task DeleteAsync(int contactId, CancellationToken cancellationToken = default);
        Task<byte[]> ExportCsvAsync(ContactsListQuery query, CancellationToken cancellationToken = default);
    }
}
