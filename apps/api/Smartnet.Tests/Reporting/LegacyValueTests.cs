using FluentAssertions;
using Smartnet.Infrastructure.Reporting;

namespace Smartnet.Tests.Reporting;

/// <summary>
/// The parser the whole reporting spine rests on. The legacy reports throw on the values these tests
/// feed it; the point is that this one does not.
/// </summary>
public sealed class LegacyValueTests
{
    [Theory]
    [InlineData("1234.56", 1234.56)]
    [InlineData("0", 0)]
    [InlineData("1,234.56", 1234.56)] // thousands separators, as some legacy values carry
    [InlineData("-50", -50)]          // a credit note / correction
    [InlineData("  42  ", 42)]        // surrounding whitespace
    public void Money_parses_a_numeric_string(string raw, decimal expected)
    {
        var value = LegacyValue.Money(raw, out var ok);

        value.Should().Be(expected);
        ok.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Money_treats_blank_as_a_legitimate_zero(string? raw)
    {
        var value = LegacyValue.Money(raw, out var ok);

        value.Should().Be(0m);
        // Blank is not a defect — an unset cost is a real, common zero. Flagging it would drown the
        // genuine problems the flag exists to surface.
        ok.Should().BeTrue();
    }

    [Theory]
    [InlineData("N/A")]
    [InlineData("abc")]
    [InlineData("12.3.4")]
    public void Money_flags_non_numeric_text_and_never_throws(string raw)
    {
        // This is the exact value double.Parse chokes on in the legacy export, taking the whole
        // report with it.
        var value = LegacyValue.Money(raw, out var ok);

        value.Should().Be(0m);
        ok.Should().BeFalse();
    }

    [Theory]
    [InlineData("2026-07-15", 2026, 7, 15)]
    [InlineData("2026-7-5", 2026, 7, 5)]
    public void Date_parses_the_legacy_iso_format(string raw, int year, int month, int day)
    {
        LegacyValue.Date(raw).Should().Be(new DateOnly(year, month, day));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("not-a-date")]
    [InlineData("15/07/2026")] // not the stored format; treated as unreadable, not misparsed
    public void Date_returns_null_for_blank_or_unreadable(string? raw)
    {
        LegacyValue.Date(raw).Should().BeNull();
    }
}
