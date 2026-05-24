using ChatCRM.Application.Contacts.Import;

namespace ChatCRM.Tests.Contacts.Import;

public class ContactImportParserTests
{
    // ── Header handling ────────────────────────────────────────────────

    [Fact]
    public void MissingPhoneNumberHeader_ProducesError_AndReturnsNoRows()
    {
        var headers = new string?[] { "First Name", "Last Name", "Email" };
        var data = new[]
        {
            (sourceRow: 2, cells: (IReadOnlyList<string?>)new string?[] { "Jane", "Doe", "j@x.com" })
        };

        var result = ContactImportParser.Parse(headers, data);

        Assert.Empty(result.Rows);
        Assert.Single(result.Errors);
        Assert.Equal(ExcelColumns.PhoneNumber, result.Errors[0].Field);
    }

    [Fact]
    public void HeadersAreMatchedCaseAndWhitespaceInsensitively()
    {
        // "phone_number", "PHONE NUMBER", "  Phone Number  " all canonicalize to the same key.
        var headers = new string?[] { "first name", "PHONE_NUMBER", "  Email  " };
        var data = new[]
        {
            (2, (IReadOnlyList<string?>)new string?[] { "Jane", "555-1234", "j@x.com" })
        };

        var result = ContactImportParser.Parse(headers, data);

        Assert.Single(result.Rows);
        Assert.Equal("Jane", result.Rows[0].FirstName);
        Assert.Equal("555-1234", result.Rows[0].PhoneNumber);
        Assert.Equal("j@x.com", result.Rows[0].Email);
    }

    [Fact]
    public void ColumnsCanBeReorderedFreely()
    {
        var headers = new string?[] { "Email", "Phone Number", "First Name", "Notes" };
        var data = new[]
        {
            (2, (IReadOnlyList<string?>)new string?[] { "j@x.com", "555", "Jane", "Hello" })
        };

        var result = ContactImportParser.Parse(headers, data);

        var row = Assert.Single(result.Rows);
        Assert.Equal("Jane", row.FirstName);
        Assert.Equal("555", row.PhoneNumber);
        Assert.Equal("j@x.com", row.Email);
        Assert.Equal("Hello", row.Notes);
    }

    [Fact]
    public void ExtraUnknownColumnsAreIgnored()
    {
        var headers = new string?[] { "Phone Number", "First Name", "CRM ID", "Source" };
        var data = new[]
        {
            (2, (IReadOnlyList<string?>)new string?[] { "555", "Jane", "X-99", "Web" })
        };

        var result = ContactImportParser.Parse(headers, data);

        Assert.Single(result.Rows);
        Assert.Equal("Jane", result.Rows[0].FirstName);
        Assert.Equal("555", result.Rows[0].PhoneNumber);
    }

    // ── Row handling ───────────────────────────────────────────────────

    [Fact]
    public void WhitespaceIsTrimmedFromEveryCell()
    {
        var headers = new string?[] { "First Name", "Phone Number", "Email" };
        var data = new[]
        {
            (2, (IReadOnlyList<string?>)new string?[] { "  Jane  ", " 555-1234 ", "  j@x.com\t" })
        };

        var result = ContactImportParser.Parse(headers, data);

        var row = Assert.Single(result.Rows);
        Assert.Equal("Jane", row.FirstName);
        Assert.Equal("555-1234", row.PhoneNumber);
        Assert.Equal("j@x.com", row.Email);
    }

    [Fact]
    public void FullyEmptyRowsAreSkipped()
    {
        var headers = new string?[] { "First Name", "Phone Number" };
        var data = new[]
        {
            (2, (IReadOnlyList<string?>)new string?[] { "Jane", "555" }),
            (3, (IReadOnlyList<string?>)new string?[] { "", "  " }),       // all blank → skip
            (4, (IReadOnlyList<string?>)new string?[] { null, null }),       // all null → skip
            (5, (IReadOnlyList<string?>)new string?[] { "Bob", "777" })
        };

        var result = ContactImportParser.Parse(headers, data);

        Assert.Equal(2, result.Rows.Count);
        Assert.Equal("Jane", result.Rows[0].FirstName);
        Assert.Equal("Bob", result.Rows[1].FirstName);
        // SourceRow must reflect the spreadsheet row number, not a re-numbered counter.
        Assert.Equal(2, result.Rows[0].SourceRow);
        Assert.Equal(5, result.Rows[1].SourceRow);
    }

    [Fact]
    public void MissingOptionalColumnsResultInNullValues()
    {
        // Email/Company/etc not in the header — those properties should be null on the parsed row.
        var headers = new string?[] { "Phone Number", "First Name" };
        var data = new[]
        {
            (2, (IReadOnlyList<string?>)new string?[] { "555", "Jane" })
        };

        var result = ContactImportParser.Parse(headers, data);

        var row = Assert.Single(result.Rows);
        Assert.Null(row.Email);
        Assert.Null(row.Company);
        Assert.Null(row.JobTitle);
        Assert.Null(row.Address);
        Assert.Null(row.Notes);
        Assert.Null(row.LastName);
    }

    [Fact]
    public void RequiredAsterisksInHeadersAreStrippedByCanonicalization()
    {
        // The template writes "Phone Number *" (with the asterisk and surrounding space) in
        // required headers. Round-tripping that should still match.
        var headers = new string?[] { "Phone Number *", "First Name" };
        var data = new[]
        {
            (2, (IReadOnlyList<string?>)new string?[] { "555", "Jane" })
        };

        var result = ContactImportParser.Parse(headers, data);

        var row = Assert.Single(result.Rows);
        Assert.Equal("555", row.PhoneNumber);
    }
}
