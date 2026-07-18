namespace Smartnet.Infrastructure.Pdf;

/// <summary>
/// A company's document colour — the one place it is decided, for every document that company prints.
/// </summary>
/// <remarks>
/// Every document a company sends should look like it came from the same business. That failed the first
/// time it was tested: the job sheets carry their accent as a constant inside each template, while the
/// quotation read <c>companies.brand_colour</c> — which holds navy for Smart Net — so the same company's
/// job sheet printed burgundy and its quotation navy.
///
/// <para><b>Not read from <c>brand_colour</c>.</b> That column disagrees with the approved job sheets,
/// and a colour that varies by which document you happen to open is worse than one that is wrong
/// everywhere. When the column is corrected this becomes the place that reads it.</para>
/// </remarks>
public static class CompanyTheme
{
    /// <summary>The seeded company whose documents are burgundy; every other company prints navy.</summary>
    private const long SmartNetCompanyId = 2;

    /// <summary>Smart Net — matches <see cref="SmartNetJobSheetDocument"/>.</summary>
    public const string SmartNetAccent = "#6B1730";

    /// <summary>Everyone else — matches <see cref="JobSheetDocument"/>.</summary>
    public const string DefaultAccent = "#1F3A5F";

    /// <summary>
    /// The section-header wash for each accent, as literals rather than a computed blend.
    /// </summary>
    /// <remarks>
    /// A blend was tried and produced <c>#EDE3E6</c> against the job sheet's <c>#F5E1E7</c> — close in
    /// value but noticeably greyer, so the same section header printed one shade on a job sheet and
    /// another on a quotation. These tints are chosen, not derived, and the burgundy one is the job
    /// sheet's own constant.
    /// </remarks>
    public const string SmartNetTint = "#F5E1E7";

    /// <summary>The navy equivalent — the same lightness, keeping its blue cast.</summary>
    public const string DefaultTint = "#E7ECF3";

    /// <summary>The accent this company's documents print in.</summary>
    public static string AccentFor(long companyId) =>
        companyId == SmartNetCompanyId ? SmartNetAccent : DefaultAccent;

    /// <summary>The section-header wash that goes with <see cref="AccentFor"/>.</summary>
    public static string TintFor(long companyId) =>
        companyId == SmartNetCompanyId ? SmartNetTint : DefaultTint;

    /// <summary>The wash for an accent, for a caller that has the colour but not the company.</summary>
    public static string TintOf(string accent) =>
        string.Equals(accent, SmartNetAccent, StringComparison.OrdinalIgnoreCase) ? SmartNetTint : DefaultTint;
}
