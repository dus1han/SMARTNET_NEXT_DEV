namespace Smartnet.Domain.Documents;

/// <summary>One line of a purchase order being created — the browser's draft, server-side.</summary>
/// <param name="ItemId">The stock item, or null for a free-typed service line.</param>
/// <param name="Cost">The line's cost basis — item lines only; carried for the future goods receipt.</param>
public sealed record NewPurchaseOrderLine(
    long? ItemId,
    string? ItemCode,
    string? Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal DiscountPercent,
    decimal? Cost);

/// <summary>A whole purchase order, posted at once — the session cart is gone (D4), the same as an invoice.</summary>
/// <param name="DocumentDiscountPercent">
/// A discount on the whole document, after any per-line discounts and before VAT. 0 for none.
/// </param>
public sealed record NewPurchaseOrder(
    long CompanyId,
    long SupplierId,
    DateOnly Date,
    IReadOnlyList<NewPurchaseOrderLine> Lines,
    decimal DocumentDiscountPercent = 0m);

/// <summary>What the caller gets back — enough to show a toast and route to the new PO.</summary>
public sealed record PurchaseOrderCreated(long Id, string Number, decimal Total);

/// <summary>
/// Creates a purchase order — the whole of it, in one transaction (Phase 6, slice 1).
/// </summary>
/// <remarks>
/// The quotation pipeline, addressed to a supplier: the tax engine values the lines, the number allocator
/// reserves the number under a row lock, the header and lines are written with their legacy shadow columns
/// beside them, and a version-1 snapshot is taken — <b>all or none</b>. There is <b>no ledger charge and
/// no stock receipt</b>: a PO is an order, not a payable and not a receipt (the payable is the supplier
/// invoice; the receipt is the deferred GRN). The number is reserved <i>inside</i> the transaction, so a
/// failed save rolls it back rather than burning it (B4).
/// </remarks>
public interface IPurchaseOrderCreator
{
    Task<PurchaseOrderCreated> CreateAsync(NewPurchaseOrder request, CancellationToken cancellationToken = default);
}
