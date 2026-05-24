using ChatCRM.Application.Contacts.Import;
using ChatCRM.Application.Interfaces;
using ChatCRM.Domain.Entities;
using ChatCRM.Persistence;
using ClosedXML.Excel;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ChatCRM.Infrastructure.Services
{
    public class ContactImportService : IContactImportService
    {
        private readonly AppDbContext _db;
        private readonly IValidator<ImportRowDto> _validator;
        private readonly ILogger<ContactImportService> _logger;

        public ContactImportService(
            AppDbContext db,
            IValidator<ImportRowDto> validator,
            ILogger<ContactImportService> logger)
        {
            _db = db;
            _validator = validator;
            _logger = logger;
        }

        // ── Template ────────────────────────────────────────────────────────

        public Task<byte[]> GenerateTemplateAsync(CancellationToken cancellationToken = default)
        {
            using var workbook = new XLWorkbook();
            var sheet = workbook.Worksheets.Add("Contacts");

            // Header row — bold, dark fill, white text. Required columns get a red corner mark.
            for (var i = 0; i < ExcelColumns.All.Length; i++)
            {
                var name = ExcelColumns.All[i];
                var cell = sheet.Cell(1, i + 1);
                var isRequired = ExcelColumns.Required.Contains(name);

                cell.Value = isRequired ? $"{name} *" : name;
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Fill.BackgroundColor = isRequired ? XLColor.DarkRed : XLColor.DarkSlateGray;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;

                if (isRequired)
                    cell.GetComment().AddText("Required");
            }

            // Example row — easy to delete, shows expected format.
            sheet.Cell(2, 1).Value = "Jane";                    // First Name
            sheet.Cell(2, 2).Value = "Doe";                     // Last Name
            sheet.Cell(2, 3).Value = "972501234567";            // Phone Number
            sheet.Cell(2, 4).Value = "jane@example.com";        // Email
            sheet.Cell(2, 5).Value = "Acme Inc.";               // Company
            sheet.Cell(2, 6).Value = "Sales Manager";           // Job Title
            sheet.Cell(2, 7).Value = "123 Main St, City";       // Address
            sheet.Cell(2, 8).Value = "Met at the trade show.";  // Notes

            // Italic + light grey so the example reads as "delete me".
            var exampleRange = sheet.Range(2, 1, 2, ExcelColumns.All.Length);
            exampleRange.Style.Font.Italic = true;
            exampleRange.Style.Font.FontColor = XLColor.Gray;

            sheet.SheetView.FreezeRows(1);
            sheet.Columns().AdjustToContents();
            // AdjustToContents leaves Notes very wide if the example is long — cap widths.
            foreach (var col in sheet.ColumnsUsed())
                if (col.Width > 40) col.Width = 40;

            using var ms = new MemoryStream();
            workbook.SaveAs(ms);
            return Task.FromResult(ms.ToArray());
        }

        // ── Preview ─────────────────────────────────────────────────────────

        public async Task<ImportPreviewDto> PreviewAsync(Stream fileStream, string fileName, CancellationToken cancellationToken = default)
        {
            var preview = new ImportPreviewDto();

            // Read the file into memory — ClosedXML wants a seekable stream and the upload
            // stream is forward-only on Kestrel.
            using var buffered = new MemoryStream();
            await fileStream.CopyToAsync(buffered, cancellationToken);
            buffered.Position = 0;

            XLWorkbook workbook;
            try
            {
                workbook = new XLWorkbook(buffered);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to open uploaded spreadsheet '{File}'", fileName);
                preview.Errors.Add(new RowError
                {
                    SourceRow = 0,
                    Field = "File",
                    Message = "The uploaded file could not be opened as an Excel workbook."
                });
                return preview;
            }

            try
            {
                var sheet = workbook.Worksheets.FirstOrDefault();
                if (sheet is null)
                {
                    preview.Errors.Add(new RowError { SourceRow = 0, Field = "File", Message = "The workbook contains no worksheets." });
                    return preview;
                }

                var used = sheet.RangeUsed();
                if (used is null)
                {
                    preview.Errors.Add(new RowError { SourceRow = 0, Field = "File", Message = "The worksheet is empty." });
                    return preview;
                }

                // First row = headers. RangeUsed().FirstRow() is relative to the range, so
                // we use the explicit row numbers from the sheet itself.
                var headerRowNum = used.FirstRow().RowNumber();
                var lastRowNum   = used.LastRow().RowNumber();
                var lastColNum   = used.LastColumn().ColumnNumber();

                var headerCells = sheet.Row(headerRowNum).Cells(1, lastColNum)
                    .Select(c => c.GetString())
                    .ToList();

                IEnumerable<(int sourceRow, IReadOnlyList<string?> cells)> DataRows()
                {
                    for (var r = headerRowNum + 1; r <= lastRowNum; r++)
                    {
                        var cells = sheet.Row(r).Cells(1, lastColNum)
                            .Select(c => (string?)c.GetString())
                            .ToList();
                        yield return (r, cells);
                    }
                }

                var parsed = ContactImportParser.Parse(headerCells!, DataRows());
                preview.Rows = parsed.Rows;
                preview.Errors.AddRange(parsed.Errors);
            }
            finally
            {
                workbook.Dispose();
            }

            ValidateRows(preview);
            await DetectDuplicatesAsync(preview, cancellationToken);

            preview.TotalRows       = preview.Rows.Count;
            preview.InvalidRows     = preview.Errors.Select(e => e.SourceRow).Distinct().Count();
            preview.ValidRows       = preview.Rows.Count(r => preview.Errors.All(e => e.SourceRow != r.SourceRow));
            preview.DuplicateCount  = preview.Duplicates.Count;

            return preview;
        }

        // ── Confirm ─────────────────────────────────────────────────────────

        public async Task<ImportResultDto> ConfirmAsync(ConfirmImportDto request, CancellationToken cancellationToken = default)
        {
            var result = new ImportResultDto { TotalRows = request.Rows.Count };

            // Re-validate server-side — never trust the client to have filtered errors out.
            var working = new ImportPreviewDto { Rows = request.Rows.ToList() };
            ValidateRows(working);
            await DetectDuplicatesAsync(working, cancellationToken);

            var invalidRows = working.Errors.Select(e => e.SourceRow).ToHashSet();
            var dbDupesByRow = working.Duplicates
                .Where(d => d.MatchesExistingContact)
                .ToDictionary(d => d.SourceRow);
            var earlierDupeRows = working.Duplicates
                .Where(d => d.MatchesEarlierRow)
                .Select(d => d.SourceRow)
                .ToHashSet();

            // Map existing contacts we may need to update — pre-fetch in one query.
            var phonesNeedingExisting = dbDupesByRow.Values
                .Select(d => PhoneNormalizer.Normalize(d.PhoneNumber))
                .Where(p => !string.IsNullOrEmpty(p))
                .Distinct()
                .ToList();

            var existingByPhone = phonesNeedingExisting.Count == 0
                ? new Dictionary<string, WhatsAppContact>()
                : await _db.WhatsAppContacts
                    .Where(c => phonesNeedingExisting.Contains(c.PhoneNumber))
                    .ToDictionaryAsync(c => c.PhoneNumber, cancellationToken);

            // Wrap the persistence step in a transaction so a mid-import failure leaves no
            // partial rows behind.
            await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                foreach (var row in working.Rows)
                {
                    if (invalidRows.Contains(row.SourceRow))
                    {
                        result.Failed.AddRange(working.Errors.Where(e => e.SourceRow == row.SourceRow));
                        continue;
                    }

                    // In-file duplicate collapses to the first occurrence regardless of strategy.
                    if (earlierDupeRows.Contains(row.SourceRow))
                    {
                        result.Skipped++;
                        continue;
                    }

                    var normalizedPhone = PhoneNormalizer.Normalize(row.PhoneNumber);

                    if (dbDupesByRow.TryGetValue(row.SourceRow, out _))
                    {
                        switch (request.DuplicateStrategy)
                        {
                            case DuplicateStrategy.Skip:
                                result.Skipped++;
                                continue;

                            case DuplicateStrategy.Update:
                                if (existingByPhone.TryGetValue(normalizedPhone, out var existing))
                                {
                                    ApplyRowToContact(existing, row, isUpdate: true);
                                    result.Updated++;
                                }
                                else
                                {
                                    // Race between preview and confirm — fall back to skip.
                                    result.Skipped++;
                                }
                                continue;

                            case DuplicateStrategy.ImportAnyway:
                                // The unique index on PhoneNumber will reject this — record as failure
                                // rather than letting the transaction abort silently.
                                result.Failed.Add(new RowError
                                {
                                    SourceRow = row.SourceRow,
                                    Field = ExcelColumns.PhoneNumber,
                                    Message = "A contact with this phone number already exists (unique constraint)."
                                });
                                continue;
                        }
                    }

                    var contact = new WhatsAppContact { PhoneNumber = normalizedPhone };
                    ApplyRowToContact(contact, row, isUpdate: false);
                    _db.WhatsAppContacts.Add(contact);
                    result.Imported++;
                }

                await _db.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);
            }
            catch (DbUpdateException ex)
            {
                await tx.RollbackAsync(cancellationToken);
                _logger.LogError(ex, "Contact import failed during SaveChanges — transaction rolled back.");
                throw new InvalidOperationException(
                    "Import failed while saving. No contacts were changed. Please retry.", ex);
            }

            return result;
        }

        private static void ApplyRowToContact(WhatsAppContact contact, ImportRowDto row, bool isUpdate)
        {
            var first = row.FirstName?.Trim();
            var last  = row.LastName?.Trim();
            var displayName = string.Join(" ", new[] { first, last }
                .Where(s => !string.IsNullOrWhiteSpace(s)));

            // On update, only overwrite when the import supplied a value — don't blow away
            // existing data with blanks.
            if (!isUpdate || !string.IsNullOrWhiteSpace(displayName))
                contact.DisplayName = string.IsNullOrWhiteSpace(displayName) ? contact.DisplayName : displayName;

            if (!isUpdate || !string.IsNullOrWhiteSpace(row.Email))    contact.Email    = row.Email?.Trim()    ?? contact.Email;
            if (!isUpdate || !string.IsNullOrWhiteSpace(row.Company))  contact.Company  = row.Company?.Trim()  ?? contact.Company;
            if (!isUpdate || !string.IsNullOrWhiteSpace(row.JobTitle)) contact.JobTitle = row.JobTitle?.Trim() ?? contact.JobTitle;
            if (!isUpdate || !string.IsNullOrWhiteSpace(row.Address))  contact.Address  = row.Address?.Trim()  ?? contact.Address;
            if (!isUpdate || !string.IsNullOrWhiteSpace(row.Notes))    contact.Notes    = row.Notes?.Trim()    ?? contact.Notes;
        }

        // ── Error report ────────────────────────────────────────────────────

        public byte[] BuildErrorReport(ErrorReportRequestDto request)
        {
            using var workbook = new XLWorkbook();
            var sheet = workbook.Worksheets.Add("Errors");

            // Header: source row + all original columns + a reason column.
            var headers = new List<string> { "Row #" };
            headers.AddRange(ExcelColumns.All);
            headers.Add("Reason");

            for (var i = 0; i < headers.Count; i++)
            {
                var cell = sheet.Cell(1, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.DarkSlateGray;
                cell.Style.Font.FontColor = XLColor.White;
            }

            // Group errors by source row so multiple errors on one row collapse into one cell.
            var errorsByRow = request.Errors
                .GroupBy(e => e.SourceRow)
                .ToDictionary(g => g.Key, g => string.Join("; ", g.Select(e => $"{e.Field}: {e.Message}")));

            var rowsByNumber = request.Rows.ToDictionary(r => r.SourceRow);

            var outRow = 2;
            // Only emit rows that actually failed — leave the successful ones out of the report.
            foreach (var sourceRow in errorsByRow.Keys.OrderBy(k => k))
            {
                rowsByNumber.TryGetValue(sourceRow, out var row);
                sheet.Cell(outRow, 1).Value = sourceRow;
                sheet.Cell(outRow, 2).Value = row?.FirstName ?? "";
                sheet.Cell(outRow, 3).Value = row?.LastName ?? "";
                sheet.Cell(outRow, 4).Value = row?.PhoneNumber ?? "";
                sheet.Cell(outRow, 5).Value = row?.Email ?? "";
                sheet.Cell(outRow, 6).Value = row?.Company ?? "";
                sheet.Cell(outRow, 7).Value = row?.JobTitle ?? "";
                sheet.Cell(outRow, 8).Value = row?.Address ?? "";
                sheet.Cell(outRow, 9).Value = row?.Notes ?? "";
                sheet.Cell(outRow, 10).Value = errorsByRow[sourceRow];
                outRow++;
            }

            sheet.SheetView.FreezeRows(1);
            sheet.Columns().AdjustToContents();
            foreach (var col in sheet.ColumnsUsed())
                if (col.Width > 60) col.Width = 60;

            using var ms = new MemoryStream();
            workbook.SaveAs(ms);
            return ms.ToArray();
        }

        // ── Validation & duplicate detection ─────────────────────────────────

        private void ValidateRows(ImportPreviewDto preview)
        {
            foreach (var row in preview.Rows)
            {
                var result = _validator.Validate(row);
                if (result.IsValid) continue;
                foreach (var failure in result.Errors)
                {
                    preview.Errors.Add(new RowError
                    {
                        SourceRow = row.SourceRow,
                        Field = failure.PropertyName,
                        Message = failure.ErrorMessage
                    });
                }
            }
        }

        private async Task DetectDuplicatesAsync(ImportPreviewDto preview, CancellationToken cancellationToken)
        {
            // In-file duplicates: first occurrence wins, later rows are marked.
            var firstSeenAtRow = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var row in preview.Rows)
            {
                var normalized = PhoneNormalizer.Normalize(row.PhoneNumber);
                if (string.IsNullOrEmpty(normalized)) continue;

                if (firstSeenAtRow.TryGetValue(normalized, out var earlier))
                {
                    preview.Duplicates.Add(new DuplicateInfo
                    {
                        SourceRow = row.SourceRow,
                        PhoneNumber = normalized,
                        MatchesEarlierRow = true,
                        EarlierSourceRow = earlier
                    });
                }
                else
                {
                    firstSeenAtRow[normalized] = row.SourceRow;
                }
            }

            // Against-DB duplicates: single round trip for all phones in the upload.
            var phones = firstSeenAtRow.Keys.ToList();
            if (phones.Count == 0) return;

            var existing = await _db.WhatsAppContacts
                .Where(c => phones.Contains(c.PhoneNumber))
                .Select(c => new { c.Id, c.PhoneNumber, c.DisplayName })
                .ToListAsync(cancellationToken);

            var existingByPhone = existing.ToDictionary(c => c.PhoneNumber);

            foreach (var row in preview.Rows)
            {
                var normalized = PhoneNormalizer.Normalize(row.PhoneNumber);
                if (string.IsNullOrEmpty(normalized)) continue;
                // Only flag the first occurrence per phone — the later ones are already covered
                // by the in-file duplicate entry above.
                if (firstSeenAtRow[normalized] != row.SourceRow) continue;

                if (existingByPhone.TryGetValue(normalized, out var match))
                {
                    preview.Duplicates.Add(new DuplicateInfo
                    {
                        SourceRow = row.SourceRow,
                        PhoneNumber = normalized,
                        MatchesExistingContact = true,
                        ExistingContactId = match.Id,
                        ExistingDisplayName = match.DisplayName
                    });
                }
            }
        }
    }
}
