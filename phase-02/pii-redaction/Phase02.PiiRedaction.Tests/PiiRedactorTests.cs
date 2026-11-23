using Phase02.PiiRedaction.Core;
using Xunit;

namespace Phase02.PiiRedaction.Tests;

public class PiiRedactorTests
{
    [Fact]
    public void Masks_an_email()
    {
        var counts = new Dictionary<string, int>();

        var result = PiiRedactor.Redact("Write to ana.petrovska@example.com today.", counts);

        Assert.Equal("Write to [EMAIL] today.", result);
        Assert.Equal(1, counts["EMAIL"]);
    }

    [Theory]
    [InlineData("MK07250120000058984")]
    [InlineData("MK07 2501 2000 0058 984")]
    public void Masks_an_iban_with_or_without_spaces(string iban)
    {
        var counts = new Dictionary<string, int>();

        var result = PiiRedactor.Redact($"Salary goes to {iban} every month.", counts);

        Assert.Equal("Salary goes to [IBAN] every month.", result);
        Assert.Equal(1, counts["IBAN"]);
    }

    [Fact]
    public void Masks_an_embg()
    {
        var counts = new Dictionary<string, int>();

        var result = PiiRedactor.Redact("EMBG 0101990450006 is on file.", counts);

        Assert.Equal("EMBG [EMBG] is on file.", result);
        Assert.Equal(1, counts["EMBG"]);
    }

    [Fact]
    public void Leaves_payroll_amounts_and_dates_alone()
    {
        const string text = "Gross 2221.72, net 1377.47, period 2026-11-01, employee 42.";
        var counts = new Dictionary<string, int>();

        var result = PiiRedactor.Redact(text, counts);

        Assert.Equal(text, result);
        Assert.Empty(counts);
    }

    [Fact]
    public void Ignores_13_digits_that_cannot_be_a_date()
    {
        // "34" can't be a month, so this can't be an EMBG
        var counts = new Dictionary<string, int>();

        var result = PiiRedactor.Redact("Order 1234567890123 shipped.", counts);

        Assert.Equal("Order 1234567890123 shipped.", result);
    }

    [Fact]
    public void Masks_a_reference_number_that_looks_like_an_embg()
    {
        // Known false positive: a date-shaped 13-digit reference is masked too.
        // For personal data, masking too much is the safer mistake.
        var counts = new Dictionary<string, int>();

        var result = PiiRedactor.Redact("Invoice 0111202600042 is paid.", counts);

        Assert.Equal("Invoice [EMBG] is paid.", result);
    }

    [Fact]
    public void Counts_every_kind_separately()
    {
        var counts = new Dictionary<string, int>();

        PiiRedactor.Redact("a@example.com, b@example.com, MK07250120000058984, 0101990450006", counts);

        Assert.Equal(2, counts["EMAIL"]);
        Assert.Equal(1, counts["IBAN"]);
        Assert.Equal(1, counts["EMBG"]);
    }
}