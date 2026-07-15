using System.Globalization;

namespace Smartnet.Domain.Settings;

/// <summary>
/// Renders a document-number prefix, which is a template the administrator controls.
/// </summary>
/// <remarks>
/// The prefix is a template rather than a literal because the live data proves it is not a
/// constant: company 1 has used <c>STI-</c> throughout, while company 2 moved from <c>SNI-</c> to
/// <c>26JUL_SNIN_</c> on 6 July 2026 — a prefix that encodes the year and the month. Storing that
/// literally would still be stamping JUL on invoices in August.
///
/// <para>So the prefix supports tokens. <c>{YY}{MON}_SNIN_</c> renders <c>26JUL_SNIN_</c> in July
/// and <c>26AUG_SNIN_</c> in August, with no deployment and no edit. A prefix with no tokens —
/// <c>STI-</c> — is simply a template that does not change, which is how a literal prefix falls
/// out of the same mechanism rather than needing its own.</para>
///
/// <para><b>The counter is independent of the prefix and never resets.</b> That is what the data
/// does: SNI-1556 was followed by 26JUL_SNIN_1562, straight through the rename. A number therefore
/// still identifies one document on its own, which is worth keeping.</para>
/// </remarks>
public static class DocumentNumberFormat
{
    /// <summary>Two-digit year: 2026 → "26".</summary>
    public const string Year2 = "{YY}";

    /// <summary>Four-digit year: 2026 → "2026".</summary>
    public const string Year4 = "{YYYY}";

    /// <summary>Three-letter month, upper case: July → "JUL". This is the one in use today.</summary>
    public const string MonthName = "{MON}";

    /// <summary>Two-digit month: July → "07".</summary>
    public const string MonthNumber = "{MM}";

    public static readonly IReadOnlyList<string> Tokens = [Year2, Year4, MonthName, MonthNumber];

    /// <summary>
    /// Expands the tokens in <paramref name="prefix"/> for a given date.
    /// </summary>
    /// <param name="on">
    /// The document's own date, not today's. Back-dating an invoice into last month must produce
    /// last month's prefix, or the number contradicts the date printed beside it.
    /// </param>
    public static string RenderPrefix(string prefix, DateOnly on)
    {
        if (string.IsNullOrEmpty(prefix))
        {
            return string.Empty;
        }

        // InvariantCulture throughout: the month abbreviation must be JUL on every server, not
        // juil. or 7月, whatever locale the host happens to be configured with.
        return prefix
            .Replace(Year4, on.Year.ToString("D4", CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace(Year2, (on.Year % 100).ToString("D2", CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace(MonthName, MonthAbbreviation(on), StringComparison.Ordinal)
            .Replace(MonthNumber, on.Month.ToString("D2", CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    /// <summary>The full number, as it appears on the document.</summary>
    public static string Render(string prefix, long number, int padding, DateOnly on) =>
        RenderPrefix(prefix, on)
        + number.ToString(CultureInfo.InvariantCulture).PadLeft(Math.Max(padding, 0), '0');

    public static bool HasTokens(string? prefix) =>
        prefix is not null && Tokens.Any(t => prefix.Contains(t, StringComparison.Ordinal));

    /// <summary>
    /// Turns an observed literal prefix back into a template, where the literal plainly contains
    /// the date of the documents that used it.
    /// </summary>
    /// <remarks>
    /// Used only to <i>propose</i> a template when initialising from the legacy data — the eight
    /// invoices prefixed <c>26JUL_SNIN_</c> are all dated July 2026, so the "26JUL" in them is
    /// evidently the date and not part of the name. It is a suggestion an administrator confirms,
    /// never something applied silently: guessing wrong here renames every future invoice.
    /// </remarks>
    public static string Templatise(string prefix, DateOnly on)
    {
        if (string.IsNullOrEmpty(prefix) || HasTokens(prefix))
        {
            return prefix;
        }

        // Longest first: replacing "26" before "2026" would turn 2026 into {YY}26.
        var templated = prefix
            .Replace(on.Year.ToString("D4", CultureInfo.InvariantCulture), Year4, StringComparison.Ordinal)
            .Replace(MonthAbbreviation(on), MonthName, StringComparison.OrdinalIgnoreCase);

        templated = templated
            .Replace((on.Year % 100).ToString("D2", CultureInfo.InvariantCulture), Year2, StringComparison.Ordinal);

        return templated;
    }

    private static string MonthAbbreviation(DateOnly on) => CultureInfo.InvariantCulture
        .DateTimeFormat
        .GetAbbreviatedMonthName(on.Month)
        .ToUpperInvariant();
}
