using System.Globalization;

namespace Smartnet.Domain.Reporting;

/// <summary>
/// The date window a report covers, and the one thing every report filter shares.
/// </summary>
/// <remarks>
/// The bounds ride the <i>request</i>, never the session. The legacy reports round-trip their filters
/// through <c>Session</c> between the search call and the export call, which is both a stale-filter
/// bug and — in <c>CustomerVATRController</c> — a real one, where the end-date is written where the
/// company id belongs and the export's company filter comes out corrupt. A report that takes its
/// parameters on the request cannot have that class of bug.
///
/// <para>The company is <i>not</i> here: it is the ambient company the user is signed into (the shell
/// switcher, validated against their token), applied by the controller. What varies per report is the
/// date column and any report-specific field; the period is the constant.</para>
/// </remarks>
public sealed record ReportPeriod(DateOnly? From, DateOnly? To)
{
    /// <summary>An unbounded period — every date passes.</summary>
    public static readonly ReportPeriod All = new(null, null);

    private string? FromText => From?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private string? ToText => To?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>
    /// Whether a legacy ISO date string falls in the window, by the same ordinal string comparison the
    /// legacy SQL used (<c>indate &gt;= 'from' AND indate &lt;= 'to'</c>).
    /// </summary>
    /// <remarks>
    /// Deliberately a string comparison, not a parse-then-compare: it reproduces exactly which rows the
    /// legacy report selected, blanks and malformed values included (they sort below any real ISO date,
    /// so a bounded period excludes them — as it did before). The row is still <i>parsed</i> for
    /// display, and flagged if that fails; selection and presentation are kept separate on purpose.
    /// </remarks>
    public bool ContainsIso(string? isoDate)
    {
        var value = isoDate ?? string.Empty;

        if (FromText is { } from && string.CompareOrdinal(value, from) < 0)
        {
            return false;
        }

        if (ToText is { } to && string.CompareOrdinal(value, to) > 0)
        {
            return false;
        }

        return true;
    }
}
