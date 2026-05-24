using ChatCRM.Application.Contacts.Import;

namespace ChatCRM.Tests.Contacts.Import;

public class ImportRowValidatorTests
{
    private readonly ImportRowValidator _validator = new();

    // ── Required field ─────────────────────────────────────────────────

    [Fact]
    public void PhoneNumber_Empty_Fails()
    {
        var row = new ImportRowDto { SourceRow = 2, PhoneNumber = "" };
        var result = _validator.Validate(row);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(row.PhoneNumber));
    }

    [Fact]
    public void PhoneNumber_TooShortAfterNormalization_Fails()
    {
        // Only 4 digits after stripping symbols — fails the 6-digit minimum.
            var row = new ImportRowDto { SourceRow = 2, PhoneNumber = "+1-2-3-4" };
        var result = _validator.Validate(row);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(row.PhoneNumber));
    }

    [Fact]
    public void PhoneNumber_ValidWithSymbols_Passes()
    {
        var row = new ImportRowDto { SourceRow = 2, PhoneNumber = "+972 (50) 123-4567" };
        var result = _validator.Validate(row);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void EverythingElseOptional_OnlyPhone_Passes()
    {
        // No first name, no email, no nothing — should still validate.
        var row = new ImportRowDto { SourceRow = 2, PhoneNumber = "972501234567" };
        var result = _validator.Validate(row);
        Assert.True(result.IsValid);
    }

    // ── Email format ───────────────────────────────────────────────────

    [Fact]
    public void Email_Blank_Passes()
    {
        var row = new ImportRowDto { SourceRow = 2, PhoneNumber = "555-1234", Email = "" };
        Assert.True(_validator.Validate(row).IsValid);
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("missing-at-sign.com")]
    [InlineData("no@dot")]
    [InlineData("@nouser.com")]
    [InlineData("with spaces@bad.com")]
    public void Email_Invalid_Fails(string email)
    {
        var row = new ImportRowDto { SourceRow = 2, PhoneNumber = "555-1234", Email = email };
        var result = _validator.Validate(row);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(row.Email));
    }

    [Theory]
    [InlineData("simple@example.com")]
    [InlineData("with.dots@sub.domain.co.uk")]
    [InlineData("plus+tag@gmail.com")]
    public void Email_Valid_Passes(string email)
    {
        var row = new ImportRowDto { SourceRow = 2, PhoneNumber = "555-1234", Email = email };
        Assert.True(_validator.Validate(row).IsValid);
    }

    // ── Phone normalization helper ─────────────────────────────────────

    [Theory]
    [InlineData("+972 (50) 123-4567", "972501234567")]
    [InlineData("555.1234", "5551234")]
    [InlineData("  ", "")]
    [InlineData(null, "")]
    [InlineData("abc-XYZ-1.2/3", "123")]
    public void PhoneNormalizer_StripsEverythingButDigits(string? raw, string expected)
    {
        Assert.Equal(expected, PhoneNormalizer.Normalize(raw));
    }

    // ── Length caps ────────────────────────────────────────────────────

    [Fact]
    public void FirstName_TooLong_Fails()
    {
        var row = new ImportRowDto
        {
            SourceRow = 2,
            PhoneNumber = "555-1234",
            FirstName = new string('x', 101)
        };
        var result = _validator.Validate(row);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(row.FirstName));
    }
}
