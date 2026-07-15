using Smartnet.Domain.Settings;

namespace Smartnet.Domain.Documents;

/// <summary>
/// Where rounding happens — the <c>app_settings</c> toggle, settled to <see cref="PerLine"/> by
/// default (PHASE-5-PLAN §slice 0-D).
/// </summary>
/// <remarks>
/// The distinction is not cosmetic. <see cref="PerLine"/> rounds each line's tax to the minor unit and
/// sums the rounded figures, so the numbers printed against each line re-add to the document total —
/// the thing a customer checks with a calculator. <see cref="PerDocument"/> sums the exact line values
/// and rounds once at the foot, which is a hair more "accurate" and whose line figures do not always
/// re-sum. The legacy app did neither deliberately: it rounded nothing and let binary <c>double</c>
/// drift decide.
/// </remarks>
public enum TaxRounding
{
    PerLine,
    PerDocument,
}

/// <summary>One line as handed to the engine — quantities and prices, not yet money.</summary>
/// <remarks>
/// A line does <b>not</b> carry a tax rate. The business uses one rate per document — the selected
/// company's — applied to every line (see the <c>one-vat-rate-per-document</c> decision, 2026-07-15);
/// the engine resolves that single rate on <see cref="TaxCalculationRequest.DocumentDate"/> and applies
/// it here.
/// </remarks>
public sealed record TaxLineInput(
    decimal Quantity,
    decimal UnitPrice,
    decimal DiscountPercent);

/// <summary>Everything the engine needs, and nothing it should fetch itself — it is pure.</summary>
/// <param name="DocumentDate">The date the rate is resolved as of. Not "today".</param>
/// <param name="IsVatRegistered">
/// A non-registered entity (Smart Technologies) is taxed at 0% — it may not charge VAT it is not
/// registered to collect. A registered entity (Smart Net) takes its default effective rate.
/// </param>
/// <param name="AvailableRates">
/// The company's configured rates. The engine picks the single one effective on
/// <see cref="DocumentDate"/> (the default); it does not read the database.
/// </param>
public sealed record TaxCalculationRequest(
    DateOnly DocumentDate,
    bool IsVatRegistered,
    TaxRounding Rounding,
    IReadOnlyList<TaxLineInput> Lines,
    IReadOnlyList<TaxRate> AvailableRates);

/// <summary>One line, computed. The rate that produced <see cref="Tax"/> is on the document, not here.</summary>
public sealed record TaxLineResult(
    int Index,
    decimal Quantity,
    decimal UnitPrice,
    decimal DiscountPercent,
    decimal Gross,
    decimal Discount,
    decimal Net,
    decimal Tax,
    decimal Total);

/// <summary>The document totals. <see cref="Total"/> is what the customer owes.</summary>
public sealed record TaxTotals(
    decimal Subtotal,
    decimal Discount,
    decimal Net,
    decimal Tax,
    decimal Total);

/// <summary>
/// The whole computation — the one rate applied, the lines, and the totals.
/// </summary>
/// <param name="TaxRateId">The resolved rate row, or null when the company is not VAT-registered.</param>
/// <param name="TaxRateName">Snapshotted onto the document — "VAT 18%" or "No VAT".</param>
/// <param name="TaxRatePercentage">Snapshotted, so a reprint reproduces the figure it was issued with.</param>
public sealed record TaxCalculationResult(
    long? TaxRateId,
    string TaxRateName,
    decimal TaxRatePercentage,
    IReadOnlyList<TaxLineResult> Lines,
    TaxTotals Totals);

/// <summary>
/// The company has no rate in force on the document date.
/// </summary>
/// <remarks>
/// Thrown, not swallowed. The legacy app resolves a missing rate to 0 and issues the document anyway;
/// here it is a validation failure the caller turns into a 400, because an invoice taxed at a rate that
/// does not exist is not a document anyone should be able to save.
/// </remarks>
public sealed class TaxRateNotResolvableException(string message) : Exception(message);
