using FluentAssertions;
using Smartnet.Infrastructure.Pdf;

namespace Smartnet.Tests.Pdf;

/// <summary>
/// The amount in words on a cheque, which is the half a bank pays against when the two disagree.
/// </summary>
public sealed class AmountInWordsTests
{
    [Fact]
    public void Writes_the_cents_the_printed_sample_lost()
    {
        // The sample cheque: 438,676.80 printed as "...AND EIGHT CENTS ONLY" by the old system, which
        // read the first decimal digit as the whole cents figure. Eighty cents, not eight.
        AmountInWords.Cheque(438676.80m).Should()
            .Be("FOUR HUNDRED THIRTY EIGHT THOUSAND SIX HUNDRED SEVENTY SIX AND EIGHTY CENTS ONLY");
    }

    [Fact]
    public void Eighty_cents_and_eight_cents_do_not_word_the_same()
    {
        // The reason the old bug mattered: under it these two amounts produced an identical cheque, and
        // the words are what gets paid.
        AmountInWords.Cheque(438676.80m).Should().NotBe(AmountInWords.Cheque(438676.08m));
        AmountInWords.Cheque(438676.08m).Should().EndWith("AND EIGHT CENTS ONLY");
    }

    [Theory]
    [InlineData(1000, "ONE THOUSAND ONLY")]
    [InlineData(20, "TWENTY ONLY")]
    [InlineData(115, "ONE HUNDRED FIFTEEN ONLY")]
    [InlineData(100000, "ONE HUNDRED THOUSAND ONLY")]
    [InlineData(1000000, "ONE MILLION ONLY")]
    public void A_whole_amount_carries_no_cents_clause(int amount, string expected) =>
        AmountInWords.Cheque(amount).Should().Be(expected);

    [Fact]
    public void An_amount_under_a_rupee_does_not_name_the_rupees()
    {
        // "FIFTY CENTS ONLY", not "ZERO AND FIFTY CENTS ONLY" — matching the legacy converter, which is
        // what the cheques already in circulation read.
        AmountInWords.Cheque(0.50m).Should().Be("FIFTY CENTS ONLY");
        AmountInWords.Cheque(0.01m).Should().Be("ONE CENTS ONLY");
    }

    [Fact]
    public void A_single_digit_of_cents_is_not_padded_into_tens()
    {
        AmountInWords.Cheque(5.05m).Should().Be("FIVE AND FIVE CENTS ONLY");
    }

    [Fact]
    public void Rounds_to_cents_before_wording_so_the_words_match_the_figures()
    {
        // The figures box rounds to two decimals; if the words did not, a cheque could read "EIGHTY" beside
        // a printed 0.81.
        AmountInWords.Cheque(0.805m).Should().Be("EIGHTY ONE CENTS ONLY");
        AmountInWords.Figures(0.805m).Should().Be("0.81");
    }

    [Fact]
    public void Words_and_figures_agree_across_the_range()
    {
        foreach (var amount in new[] { 0.01m, 0.99m, 1m, 19m, 99.99m, 1234.56m, 999999.99m })
        {
            var words = AmountInWords.Cheque(amount);

            words.Should().NotBeEmpty();
            words.Should().EndWith("ONLY");
            // Every amount with cents names them; every whole one does not.
            (amount == decimal.Truncate(amount)).Should().Be(!words.Contains("CENTS"));
        }
    }

    [Fact]
    public void A_negative_amount_is_refused_rather_than_worded()
    {
        // No cheque is drawn for a negative amount, and "MINUS FIVE ONLY" is not a thing to hand a bank.
        AmountInWords.Cheque(-5m).Should().BeEmpty();
    }
}
