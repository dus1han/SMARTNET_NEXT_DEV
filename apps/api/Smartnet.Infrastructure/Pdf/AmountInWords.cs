using System.Globalization;
using System.Text;

namespace Smartnet.Infrastructure.Pdf;

/// <summary>
/// An amount written out in words, as a cheque requires.
/// </summary>
/// <remarks>
/// <b>This is the part of a cheque that legally counts.</b> Where the words and the figures disagree, the
/// words govern — the bank pays what is written, not what is typed in the box. So this is not a
/// formatting helper; it is the amount.
///
/// <para><b>The legacy converter was wrong about cents.</b> A sample cheque for <c>438,676.80</c> printed
/// "FOUR HUNDRED THIRTY EIGHT THOUSAND SIX HUNDRED SEVENTY SIX AND <b>EIGHT</b> CENTS ONLY" — eight
/// cents, not eighty. It read the first decimal digit as the whole cents figure, so every amount ending
/// in a round ten-cent value was understated by an order of magnitude in the words, and the words are the
/// binding half. Reproducing that faithfully was never an option; this converts the full two-digit cents
/// value.</para>
///
/// <para>The wording otherwise follows the legacy house style, because these cheques are recognised by
/// the people who countersign them: upper case, no "RUPEES", no "AND" between the hundreds and the tens
/// ("FOUR HUNDRED THIRTY EIGHT"), and "ONLY" at the end to close the line against alteration.</para>
/// </remarks>
public static class AmountInWords
{
    private static readonly string[] Ones =
    [
        "", "ONE", "TWO", "THREE", "FOUR", "FIVE", "SIX", "SEVEN", "EIGHT", "NINE", "TEN",
        "ELEVEN", "TWELVE", "THIRTEEN", "FOURTEEN", "FIFTEEN", "SIXTEEN", "SEVENTEEN", "EIGHTEEN", "NINETEEN",
    ];

    private static readonly string[] Tens =
    [
        "", "", "TWENTY", "THIRTY", "FORTY", "FIFTY", "SIXTY", "SEVENTY", "EIGHTY", "NINETY",
    ];

    /// <summary>
    /// The full cheque line — "FOUR HUNDRED THIRTY EIGHT THOUSAND SIX HUNDRED SEVENTY SIX AND EIGHTY
    /// CENTS ONLY". Cents are omitted when there are none.
    /// </summary>
    public static string Cheque(decimal amount)
    {
        if (amount < 0m)
        {
            // A cheque cannot be drawn for a negative amount; say so rather than print "MINUS".
            return string.Empty;
        }

        // Round to cents first, so 0.805 does not word as "EIGHTY" while the figures box prints "0.81".
        var rounded = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
        var rupees = decimal.Truncate(rounded);
        var cents = (int)decimal.Round((rounded - rupees) * 100m, 0, MidpointRounding.AwayFromZero);

        var words = new StringBuilder(Whole(rupees));

        if (cents > 0)
        {
            words.Append(" AND ").Append(Whole(cents)).Append(" CENTS");
        }

        return words.Append(" ONLY").ToString();
    }

    /// <summary>A whole number in words, grouped the way Sri Lankan cheques are written.</summary>
    private static string Whole(decimal value)
    {
        if (value == 0m)
        {
            return "ZERO";
        }

        var parts = new List<string>();

        foreach (var (scale, name) in Scales)
        {
            var count = decimal.Truncate(value / scale);

            if (count > 0)
            {
                parts.Add(UnderThousand((int)count));
                if (name.Length > 0) parts.Add(name);
                value -= count * scale;
            }
        }

        return string.Join(" ", parts);
    }

    /// <summary>
    /// The scale words, largest first.
    /// </summary>
    /// <remarks>
    /// Deliberately the international scale (thousand / million), not the South Asian lakh and crore. The
    /// legacy converter used this scale and the cheques in circulation read this way; switching now would
    /// make two eras of the same company's cheques word the same amount differently.
    /// </remarks>
    private static readonly (decimal Scale, string Name)[] Scales =
    [
        (1_000_000_000m, "BILLION"),
        (1_000_000m, "MILLION"),
        (1_000m, "THOUSAND"),
        (1m, ""),
    ];

    private static string UnderThousand(int value)
    {
        var parts = new List<string>();

        if (value >= 100)
        {
            parts.Add(Ones[value / 100]);
            parts.Add("HUNDRED");
            value %= 100;
        }

        if (value >= 20)
        {
            parts.Add(Tens[value / 10]);
            value %= 10;
        }

        if (value > 0)
        {
            parts.Add(Ones[value]);
        }

        return string.Join(" ", parts);
    }

    /// <summary>The figures box — grouped with separators, two decimals, as the legacy cheque printed.</summary>
    public static string Figures(decimal amount) =>
        decimal.Round(amount, 2, MidpointRounding.AwayFromZero).ToString("N2", CultureInfo.InvariantCulture);
}
