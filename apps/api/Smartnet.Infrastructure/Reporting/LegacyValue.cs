using System.Globalization;

namespace Smartnet.Infrastructure.Reporting;

/// <summary>
/// Reads the legacy <c>varchar</c> columns — money and dates — into real types, once, defensively.
/// </summary>
/// <remarks>
/// This is the entire reason the reporting spine exists (Finding 5). In the legacy app every figure
/// is text in the database: <c>totamount</c>, <c>cost</c>, <c>balance</c>, <c>expense_amount</c> are
/// all <c>varchar</c>, and the reports parse them per row with <c>double.Parse(rw.Field&lt;string&gt;(…))</c>
/// — which means a single blank or malformed value throws an unhandled exception and takes the whole
/// report with it. Three legacy exports demonstrably do this.
///
/// <para>Here a bad value becomes <c>0</c> and the row is <i>flagged</i> (<c>ok: false</c>), never an
/// exception. A blank is treated as a legitimate zero rather than a defect — that is the common,
/// benign case (an unset cost, an empty reference), and flagging every blank would drown the genuine
/// data problems the flag exists to surface.</para>
///
/// <para>Everything is parsed with <see cref="CultureInfo.InvariantCulture"/>. Money must not vary
/// with the server's locale — "1.234,56" on one host and "1,234.56" on another is the same bug the
/// logging config guards against.</para>
/// </remarks>
public static class LegacyValue
{
    /// <summary>The formats <c>indate</c>/<c>expense_date</c> are stored in — ISO, confirmed against
    /// the live schema (the legacy HTML <c>&lt;input type="date"&gt;</c> submits <c>yyyy-MM-dd</c>, and
    /// every report string-compares the column against ISO bounds).</summary>
    private static readonly string[] DateFormats = ["yyyy-MM-dd", "yyyy-M-d"];

    /// <summary>Parses a legacy money column. Blank is a legitimate 0; only non-numeric text is a
    /// defect, and only then is <paramref name="ok"/> false.</summary>
    public static decimal Money(string? raw, out bool ok)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            ok = true;
            return 0m;
        }

        if (decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
        {
            ok = true;
            return value;
        }

        ok = false;
        return 0m;
    }

    /// <summary>Parses a legacy money column, discarding the flag. Use the overload with <c>out ok</c>
    /// when the row needs to be marked as carrying a data defect.</summary>
    public static decimal Money(string? raw) => Money(raw, out _);

    /// <summary>
    /// Parses a legacy ISO date column to a <see cref="DateOnly"/>, or <c>null</c> when it is blank or
    /// unreadable. The legacy <c>Split('-')</c> aging idiom throws on a malformed value; this does not.
    /// </summary>
    public static DateOnly? Date(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return DateOnly.TryParseExact(
            raw.Trim(),
            DateFormats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var date)
            ? date
            : null;
    }
}
