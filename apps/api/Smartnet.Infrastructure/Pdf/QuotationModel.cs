namespace Smartnet.Infrastructure.Pdf;

/// <summary>One priced line of a quotation — the five detail columns the legacy report carried.</summary>
public sealed record QuotationItem(
    string ItemNo,
    string Description,
    decimal Quantity,
    decimal Rate,
    decimal Total);

/// <summary>
/// The company's bank details, printed as the payment block.
/// </summary>
/// <remarks>
/// The legacy templates hardcoded "Sampath Bank – Kohuwala" inside the <c>_ST</c> report files, which is
/// why changing a bank account meant editing a report binary. It comes from the company profile now.
/// </remarks>
public sealed record BankDetails(string BankName, string? Branch, string? AccountName, string? AccountNumber);

/// <summary>
/// What a quotation renders from.
/// </summary>
/// <remarks>
/// One model for every company, unlike the legacy <c>Quotation_SN</c> / <c>Quotation_ST</c> pair. The
/// split between those two was never about branding — it was that Smart Net is VAT-registered and Smart
/// Technologies is not, so the VAT rows simply did not exist in the <c>_ST</c> file (REPORT-FIELDS.md).
/// Here <see cref="TaxAmount"/> is null when the company is not registered and the rows are omitted, so
/// the same template serves both and a company becoming VAT-registered is a settings change.
///
/// <para>Money arrives as <c>decimal</c> and is formatted at render time. The legacy report received
/// pre-formatted strings, which froze every rounding decision upstream and out of sight.</para>
/// </remarks>
public sealed record QuotationModel(
    byte[]? Logo,
    string CompanyName,
    string CompanyContact,
    string? AccentColour,
    string QuotationNo,
    string Date,
    string ClientName,
    string ClientAddress,
    string ContactPerson,
    string PreparedBy,
    string Validity,
    IReadOnlyList<QuotationItem> Items,
    decimal Subtotal,
    decimal DiscountPercent,
    decimal DiscountAmount,
    decimal NetTotal,

    /// <summary>The VAT label, e.g. "VAT (18%)" — null when the company is not VAT-registered.</summary>
    string? TaxLabel,

    /// <summary>Null when the company is not VAT-registered, which omits the VAT rows entirely.</summary>
    decimal? TaxAmount,

    decimal Total,

    /// <summary>The payment block. Null when the company has no bank details on file.</summary>
    BankDetails? Bank);
