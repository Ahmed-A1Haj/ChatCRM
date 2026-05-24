using System.Text.Json.Serialization;

namespace ChatCRM.Application.Contacts.Import
{
    /// <summary>
    /// Strategy applied to rows that duplicate an existing contact (matched on phone number).
    /// In-file duplicates always collapse to the first occurrence; this only governs the
    /// against-DB case.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum DuplicateStrategy
    {
        Skip = 0,
        Update = 1,
        ImportAnyway = 2
    }

    /// <summary>
    /// One parsed (and possibly invalid) row from the uploaded spreadsheet. Source row
    /// number is the spreadsheet row number including the header (header = row 1), so the
    /// user can locate it in their file with no mental arithmetic.
    /// </summary>
    public class ImportRowDto
    {
        public int SourceRow { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string PhoneNumber { get; set; } = string.Empty;
        public string? Email { get; set; }
        public string? Company { get; set; }
        public string? JobTitle { get; set; }
        public string? Address { get; set; }
        public string? Notes { get; set; }

        public string FullName =>
            string.Join(" ", new[] { FirstName, LastName }
                .Where(s => !string.IsNullOrWhiteSpace(s)));
    }

    public class RowError
    {
        public int SourceRow { get; set; }
        public string Field { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// Marks a parsed row as duplicating either an existing DB contact or an earlier row in
    /// the same upload. The UI surfaces this so the user can choose Skip/Update/ImportAnyway
    /// at confirm time.
    /// </summary>
    public class DuplicateInfo
    {
        public int SourceRow { get; set; }
        public string PhoneNumber { get; set; } = string.Empty;
        public bool MatchesExistingContact { get; set; }
        public int? ExistingContactId { get; set; }
        public string? ExistingDisplayName { get; set; }
        public bool MatchesEarlierRow { get; set; }
        public int? EarlierSourceRow { get; set; }
    }

    public class ImportPreviewDto
    {
        public List<ImportRowDto> Rows { get; set; } = new();
        public List<RowError> Errors { get; set; } = new();
        public List<DuplicateInfo> Duplicates { get; set; } = new();
        public int TotalRows { get; set; }
        public int ValidRows { get; set; }
        public int InvalidRows { get; set; }
        public int DuplicateCount { get; set; }
    }

    /// <summary>
    /// Payload sent by the UI when the user clicks "Confirm Import". Includes the rows the
    /// preview displayed (the server re-validates) and the chosen duplicate strategy.
    /// </summary>
    public class ConfirmImportDto
    {
        public List<ImportRowDto> Rows { get; set; } = new();
        public DuplicateStrategy DuplicateStrategy { get; set; } = DuplicateStrategy.Skip;
    }

    public class ImportResultDto
    {
        public int TotalRows { get; set; }
        public int Imported { get; set; }
        public int Updated { get; set; }
        public int Skipped { get; set; }
        public List<RowError> Failed { get; set; } = new();
    }

    /// <summary>
    /// Payload the UI POSTs back to download an Excel error report. Sent as a discrete
    /// request (rather than embedded in the import response) so the user can re-download
    /// without re-importing.
    /// </summary>
    public class ErrorReportRequestDto
    {
        public List<ImportRowDto> Rows { get; set; } = new();
        public List<RowError> Errors { get; set; } = new();
    }
}
