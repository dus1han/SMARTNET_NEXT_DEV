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
/// <para><b>Matched against the legacy converter, output for output.</b> This reproduces
/// <c>Smart_InvSys/AmountToText.cs</c> as it now stands, put through the same post-processing
/// <c>ChequeController</c> applies before printing — upper-cased, " ONLY" appended, commas removed,
/// hyphens turned into spaces. 25 amounts across the range, including every case below, were compared
/// against the legacy code compiled and run directly rather than read and reasoned about.</para>
///
/// <para><b>The bug that was in the printed sample is already fixed there.</b> A cheque for
/// <c>438,676.80</c> printed "...AND <b>EIGHT</b> CENTS ONLY" because the old code did
/// <c>n.ToString().Split('.')</c> and <c>(438676.80).ToString()</c> drops the trailing zero, leaving
/// "8". Under it <c>.80</c> and <c>.08</c> produced an identical cheque. The legacy app now computes
/// cents as hundredths, which is what this does; the superseded version is still in that file,
/// commented out beneath the working one.</para>
///
/// <para>The wording follows the legacy house style, because these cheques are recognised by the people
/// who countersign them: upper case, no "RUPEES", no "AND" between the hundreds and the tens ("FOUR
/// HUNDRED THIRTY EIGHT"), no rupee words at all below a rupee ("FIFTY CENTS ONLY"), and "ONLY" closing
/// the line against alteration.</para>
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
    /// CENTS ONLY". Cents are omitted when there are none, and rupees when the amount is under one.
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

        if (rupees == 0m && cents == 0)
        {
            return "ZERO ONLY";
        }

        var words = new StringBuilder();

        // Under a rupee, the rupee words are left out altogether: "FIFTY CENTS ONLY", not "ZERO AND
        // FIFTY CENTS ONLY". Matching the legacy converter, which reads better and is what is already
        // written on the cheques in circulation.
        if (rupees > 0m)
        {
            words.Append(Whole(rupees));
        }

        if (cents > 0)
        {
            if (words.Length > 0) words.Append(" AND ");
            words.Append(Whole(cents)).Append(" CENTS");
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
