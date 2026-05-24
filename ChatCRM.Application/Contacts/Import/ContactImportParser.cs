namespace ChatCRM.Application.Contacts.Import
{
    /// <summary>
    /// Pure (no DB / no I/O beyond the supplied row reader) parser. The spreadsheet library
    /// lives in Infrastructure; this layer only sees a sequence of rows-of-strings, which
    /// makes the logic trivially testable.
    /// </summary>
    public static class ContactImportParser
    {
        public class ParseOutput
        {
            public List<ImportRowDto> Rows { get; set; } = new();
            public List<RowError> Errors { get; set; } = new();
        }

        /// <param name="headerRow">Cells of the first non-empty row. Header names are matched
        /// case- and whitespace-insensitively (see <see cref="ExcelColumns.Canonicalize"/>).</param>
        /// <param name="dataRows">Subsequent rows. The supplied 1-based <c>sourceRow</c>
        /// should be the spreadsheet row number so error messages reference the user's file
        /// directly.</param>
        public static ParseOutput Parse(
            IReadOnlyList<string?> headerRow,
            IEnumerable<(int sourceRow, IReadOnlyList<string?> cells)> dataRows)
        {
            var output = new ParseOutput();

            // Build header -> column index map (canonicalized).
            var headerMap = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var i = 0; i < headerRow.Count; i++)
            {
                var key = ExcelColumns.Canonicalize(headerRow[i]);
                if (!string.IsNullOrEmpty(key) && !headerMap.ContainsKey(key))
                    headerMap[key] = i;
            }

            // Required header check up front. If Phone Number is missing the file is unusable.
            var phoneKey = ExcelColumns.Canonicalize(ExcelColumns.PhoneNumber);
            if (!headerMap.ContainsKey(phoneKey))
            {
                output.Errors.Add(new RowError
                {
                    SourceRow = 1,
                    Field = ExcelColumns.PhoneNumber,
                    Message = $"Required column '{ExcelColumns.PhoneNumber}' is missing from the header row."
                });
                return output;
            }

            foreach (var (sourceRow, cells) in dataRows)
            {
                // Skip rows where every visible cell is blank — common when users delete the
                // example row without clearing styling.
                if (cells.All(c => string.IsNullOrWhiteSpace(c)))
                    continue;

                var row = new ImportRowDto
                {
                    SourceRow   = sourceRow,
                    FirstName   = Read(cells, headerMap, ExcelColumns.FirstName),
                    LastName    = Read(cells, headerMap, ExcelColumns.LastName),
                    PhoneNumber = Read(cells, headerMap, ExcelColumns.PhoneNumber) ?? string.Empty,
                    Email       = Read(cells, headerMap, ExcelColumns.Email),
                    Company     = Read(cells, headerMap, ExcelColumns.Company),
                    JobTitle    = Read(cells, headerMap, ExcelColumns.JobTitle),
                    Address     = Read(cells, headerMap, ExcelColumns.Address),
                    Notes       = Read(cells, headerMap, ExcelColumns.Notes),
                };
                output.Rows.Add(row);
            }

            return output;
        }

        private static string? Read(
            IReadOnlyList<string?> cells,
            Dictionary<string, int> headerMap,
            string columnName)
        {
            var key = ExcelColumns.Canonicalize(columnName);
            if (!headerMap.TryGetValue(key, out var idx)) return null;
            if (idx < 0 || idx >= cells.Count) return null;
            var v = cells[idx]?.Trim();
            return string.IsNullOrEmpty(v) ? null : v;
        }
    }
}
