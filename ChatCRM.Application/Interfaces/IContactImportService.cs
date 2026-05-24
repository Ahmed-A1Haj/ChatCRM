using ChatCRM.Application.Contacts.Import;

namespace ChatCRM.Application.Interfaces
{
    public interface IContactImportService
    {
        /// <summary>
        /// Builds an empty .xlsx template with bold + frozen headers and one example row.
        /// Only the Phone Number column is marked required.
        /// </summary>
        Task<byte[]> GenerateTemplateAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Parses + validates the uploaded spreadsheet without persisting anything. Returns
        /// the row preview, per-row validation errors, and detected duplicates (in-file and
        /// against the contacts table).
        /// </summary>
        Task<ImportPreviewDto> PreviewAsync(Stream fileStream, string fileName, CancellationToken cancellationToken = default);

        /// <summary>
        /// Persists the supplied rows in a single transaction. Re-validates each row server-side.
        /// </summary>
        Task<ImportResultDto> ConfirmAsync(ConfirmImportDto request, CancellationToken cancellationToken = default);

        /// <summary>
        /// Renders the failed rows + reasons as an .xlsx the user can fix and re-upload.
        /// </summary>
        byte[] BuildErrorReport(ErrorReportRequestDto request);
    }
}
