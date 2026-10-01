namespace Smartnet.Domain.Reporting;

/// <summary>
/// Renders a quotation or invoice that has not been raised yet — printed from a draft, and marked DRAFT
/// on every page.
/// </summary>
/// <remarks>
/// <para><b>From the screen's figures, not the draft row.</b> A draft's payload is the create screen's own
/// state and the server never parses it (see <c>DocumentDraft</c>), so the browser reads it and sends
/// what the document would be raised with. The figures are then valued here by the same tax engine a real
/// save uses, so a draft prints the totals it would actually be raised at.</para>
///
/// <para><b>Nothing is saved.</b> No number is allocated, no row is written and nothing is audited — a
/// printed draft is a look at an offer that does not exist yet, which is exactly what its marking says.</para>
/// </remarks>
public interface IDraftDocumentRenderer
{
    /// <summary>The draft quotation as a PDF, or null if its company does not exist.</summary>
    Task<byte[]?> RenderQuotationAsync(DraftQuotationPrint draft, CancellationToken cancellationToken = default);

    /// <summary>
    /// The draft invoice as a PDF, or null if its company does not exist. A tax invoice when it would
    /// charge VAT, a plain one otherwise — the same choice a raised invoice prints by.
    /// </summary>
    Task<byte[]?> RenderInvoiceAsync(DraftInvoicePrint draft, CancellationToken cancellationToken = default);
}

/// <summary>One line of a draft, in major units.</summary>
public sealed record DraftPrintLine(string? Description, decimal Quantity, decimal UnitPrice, decimal DiscountPercent);

/// <summary>A draft quotation's content. The customer may not be chosen yet.</summary>
public sealed record DraftQuotationPrint(
    long CompanyId,
    long? CustomerId,
    DateOnly Date,
    string? ContactPerson,
    string? Validity,
    decimal DocumentDiscountPercent,
    IReadOnlyList<DraftPrintLine> Lines);

/// <summary>A draft invoice's content. The customer may not be chosen yet.</summary>
public sealed record DraftInvoicePrint(
    long CompanyId,
    long? CustomerId,
    string Type,
    DateOnly Date,
    string? PurchaseOrderNo,
    string? ContactPerson,
    decimal DocumentDiscountPercent,
    IReadOnlyList<DraftPrintLine> Lines);
