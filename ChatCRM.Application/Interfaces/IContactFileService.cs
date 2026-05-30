using ChatCRM.Application.Contacts.DTOs;

namespace ChatCRM.Application.Interfaces
{
    /// <summary>
    /// Manages private, internal-only files attached to a contact. All methods operate on private
    /// storage that is never exposed publicly — see <see cref="ChatCRM.Domain.Entities.ContactFile"/>.
    /// </summary>
    public interface IContactFileService
    {
        /// <summary>Lists a contact's private files (newest first). Empty list if the contact has none.</summary>
        Task<List<ContactFileDto>> ListAsync(int contactId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Stores one uploaded file against the contact in private storage. Validates type + size;
        /// throws <see cref="InvalidOperationException"/> with a friendly message on rejection, or
        /// <see cref="KeyNotFoundException"/> if the contact doesn't exist.
        /// </summary>
        Task<ContactFileDto> UploadAsync(
            int contactId, byte[] data, string originalFileName, string contentType,
            string? uploadedByUserId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Opens a contact file for an authorized download. Returns null if the file doesn't exist,
        /// doesn't belong to the contact, or is missing on disk.
        /// </summary>
        Task<ContactFileContent?> OpenAsync(int contactId, int fileId, CancellationToken cancellationToken = default);

        /// <summary>Renames the display name of a file (does not touch the stored bytes). Null if not found.</summary>
        Task<ContactFileDto?> RenameAsync(int contactId, int fileId, string newName, CancellationToken cancellationToken = default);

        /// <summary>Deletes the DB row and the physical file. Returns false if not found.</summary>
        Task<bool> DeleteAsync(int contactId, int fileId, CancellationToken cancellationToken = default);
    }
}
