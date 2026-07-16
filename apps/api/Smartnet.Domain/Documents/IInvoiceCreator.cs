namespace Smartnet.Domain.Documents;

/// <summary>One line of a document being created — the browser's draft, server-side.</summary>
/// <param name="ItemId">The stock item, or null for a free-typed service line.</param>
/// <param name="Cost">The line's cost basis — item lines only.</param>
public sealed record NewInvoiceLine(
    long? ItemId,
    string? ItemCode,
    string? Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal DiscountPercent,
    decimal? Cost);

/// <summary>A whole invoice, posted at once — the cart is gone (D4).</summary>
/// <param name="DocumentDiscountPercent">
/// A discount on the whole document, after any per-line discounts and before VAT. 0 for none — a
/// discount may be given per line, on the document, or both.
/// </param>
/// <param name="AcknowledgeCreditLimit">
/// The credit limit is a <b>soft</b> gate: a breach is not a dead-end. When the person raising the
/// invoice has been shown the breach and confirmed it, this is <c>true</c> and the save proceeds — the
/// confirmation is the override. It is <c>false</c> on a first, un-confirmed attempt (and on a direct
/// API call), so a breach is caught and surfaced rather than slipping through unseen.
/// </param>
/// <param name="DocumentCost">
/// A document-level cost for a <b>service</b> invoice — where cost cannot be derived from item lines, the
/// user enters one figure for the whole document (the legacy <c>invoice_h.cost</c> for service invoices).
/// Null for an <b>item</b> invoice, whose cost is the sum of the line costs carried from the item master.
/// </param>
public sealed record NewInvoice(
    long CompanyId,
    long CustomerId,
    InvoiceType Type,
    DateOnly Date,
    string? PurchaseOrderNo,
    string? ContactPerson,
    IReadOnlyList<NewInvoiceLine> Lines,
    decimal DocumentDiscountPercent = 0m,
    bool AcknowledgeCreditLimit = false,
    decimal? DocumentCost = null);

/// <summary>What the caller gets back — enough to show a toast and route to the new invoice.</summary>
public sealed record InvoiceCreated(long Id, string Number, decimal Total, decimal Outstanding);

/// <summary>
/// Creates an invoice — the whole of it, in one transaction (Phase 5, slice 1).
/// </summary>
/// <remarks>
/// This is where the pieces meet: the tax engine values the lines, the number allocator reserves the
/// number under a row lock, the header and lines are written with their legacy shadow columns beside
/// them, the receivables ledger is charged (and, for a cash invoice, settled), stock is issued, and a
/// version-1 snapshot is taken — <b>all or none</b> (B2). The number is reserved <i>inside</i> the
/// transaction, so a failed save rolls it back rather than burning it (B4).
/// </remarks>
public interface IInvoiceCreator
{
    /// <summary>
    /// Creates an invoice in its own transaction — the entry point for a hand-keyed invoice.
    /// </summary>
    Task<InvoiceCreated> CreateAsync(NewInvoice request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates an invoice <b>inside a transaction the caller already owns</b>, without beginning or
    /// committing one — the entry point for quotation conversion, which must write the invoice and mark
    /// the quote converted atomically (all or none).
    /// </summary>
    /// <param name="sourceQuotationId">
    /// The quotation being converted, stored on the invoice as its back-link; null for a direct invoice.
    /// </param>
    /// <remarks>
    /// Throws if there is no active transaction: an invoice half-written outside one is exactly the
    /// partial document (B2) the pipeline exists to prevent. <see cref="CreateAsync"/> is this method
    /// wrapped in <c>BeginTransaction … Commit</c>.
    /// </remarks>
    Task<InvoiceCreated> CreateInCurrentTransactionAsync(
        NewInvoice request,
        long? sourceQuotationId,
        CancellationToken cancellationToken = default);
}
