using System.Text;

namespace Smartnet.Api.Reporting;

/// <summary>
/// Renders a money amount as English words — "ONE THOUSAND TWO HUNDRED AND FIFTY FILS ONLY".
/// </summary>
/// <remarks>
/// The legacy cheque report left its <c>inwords</c> column blank (the assignment was commented out) and
/// derived the words only on the cheque-printing path. The report derives them here instead, from the
/// amount it already parsed — never from a stored column. Styled cheque printing itself is Phase 8;
/// this is the report's own copy of the figure.
/// </remarks>
public static class AmountInWords
{
    private static readonly string[] Ones =
    [
        "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten",
        "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen",
        "nineteen",
    ];

    private static readonly string[] Tens =
        ["", "", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety"];

    private static readonly (long Value, string Name)[] Scales =
        [(1_000_000_000, "billion"), (1_000_000, "million"), (1_000, "thousand")];

    public static string Of(decimal amount)
    {
        var negative = amount < 0;
        amount = Math.Abs(amount);

        var whole = (long)decimal.Truncate(amount);
        var fils = (int)decimal.Round((amount - whole) * 100m, MidpointRounding.AwayFromZero);

        // Rounding the fraction can carry into the whole (e.g. 9.999 → 10.00).
        if (fils == 100)
        {
            whole++;
            fils = 0;
        }

        var builder = new StringBuilder();

        if (negative)
        {
            builder.Append("minus ");
        }

        builder.Append(Words(whole));

        if (fils > 0)
        {
            builder.Append(" and ").Append(Words(fils)).Append(" fils");
        }

        builder.Append(" only");

        return builder.ToString().ToUpperInvariant();
    }

    private static string Words(long number)
    {
        if (number == 0)
        {
            return Ones[0];
        }

        var parts = new List<string>();

        foreach (var (value, name) in Scales)
        {
            if (number >= value)
            {
                parts.Add($"{Words(number / value)} {name}");
                number %= value;
            }
        }

        if (number >= 100)
        {
            parts.Add($"{Ones[number / 100]} hundred");
            number %= 100;
        }

        if (number >= 20)
        {
            var tens = Tens[number / 10];
            parts.Add(number % 10 == 0 ? tens : $"{tens} {Ones[number % 10]}");
        }
        else if (number > 0)
        {
            parts.Add(Ones[number]);
        }

        return string.Join(" ", parts);
    }
}
