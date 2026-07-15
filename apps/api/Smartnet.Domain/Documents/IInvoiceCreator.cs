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
public sealed record NewInvoice(
    long CompanyId,
    long CustomerId,
    InvoiceType Type,
    DateOnly Date,
    string? PurchaseOrderNo,
    string? ContactPerson,
    IReadOnlyList<NewInvoiceLine> Lines);

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
    Task<InvoiceCreated> CreateAsync(NewInvoice request, CancellationToken cancellationToken = default);
}
