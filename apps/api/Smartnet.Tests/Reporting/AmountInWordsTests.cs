using FluentAssertions;
using Smartnet.Api.Reporting;

namespace Smartnet.Tests.Reporting;

public sealed class AmountInWordsTests
{
    [Theory]
    [InlineData(0, "ZERO ONLY")]
    [InlineData(5, "FIVE ONLY")]
    [InlineData(19, "NINETEEN ONLY")]
    [InlineData(42, "FORTY TWO ONLY")]
    [InlineData(100, "ONE HUNDRED ONLY")]
    [InlineData(1250, "ONE THOUSAND TWO HUNDRED FIFTY ONLY")]
    [InlineData(1000000, "ONE MILLION ONLY")]
    public void It_spells_whole_amounts(double amount, string expected)
    {
        AmountInWords.Of((decimal)amount).Should().Be(expected);
    }

    [Fact]
    public void It_includes_the_fils_when_there_is_a_fraction()
    {
        AmountInWords.Of(12.50m).Should().Be("TWELVE AND FIFTY FILS ONLY");
    }

    [Fact]
    public void A_rounded_up_fraction_carries_into_the_whole()
    {
        // 9.999 → 10.00, not "nine and one hundred fils".
        AmountInWords.Of(9.999m).Should().Be("TEN ONLY");
    }
}
