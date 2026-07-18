namespace Smartnet.Domain.Documents;

/// <param name="Id">The existing line this maps to, or null for a line the edit adds.</param>
public sealed record EditPurchaseOrderLine(
    long? Id,
    long? ItemId,
    string? ItemCode,
    string? Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal DiscountPercent,
    decimal? Cost);

/// <summary>An edit to a purchase order — the whole of its editable state, posted at once.</summary>
/// <param name="ExpectedRowVersion">The row_version the editor loaded; a stale one is a conflict.</param>
/// <param name="Date">
/// The document date, or null to leave it. Changing it re-rates the order at the rate in force on the new
/// date. An order posts nothing, so there are no entries to move with it.
/// </param>
public sealed record EditPurchaseOrder(
    int ExpectedRowVersion,
    decimal DocumentDiscountPercent,
    IReadOnlyList<EditPurchaseOrderLine> Lines,
    decimal? DocumentCost = null,
    DateOnly? Date = null);

/// <summary>What an edit returns — the new figures and the version it wrote.</summary>
public sealed record PurchaseOrderEdited(long Id, string Number, decimal Total, int VersionNo);

/// <summary>
/// Edits a purchase order — versioned, reason-gated, concurrency-guarded.
/// </summary>
/// <remarks>
/// The quotation editor's mirror, addressed to a supplier: re-runs the tax engine, reconciles the lines in
/// place, writes a new version. An order posts <b>no ledger entry and no stock movement</b> — it is an
/// instruction to a supplier, not a payable or a receipt — so an edit has nothing to adjust beyond the
/// document itself. The payable is the supplier invoice; the receipt is the goods-received note.
/// </remarks>
public interface IPurchaseOrderEditor
{
    Task<PurchaseOrderEdited> EditAsync(long orderId, EditPurchaseOrder request, CancellationToken cancellationToken = default);
}

/// <summary>What a purchase-order void returns.</summary>
public sealed record PurchaseOrderDeleted(long Id, string Number);

/// <summary>
/// Voids a purchase order — soft, recoverable, attributable.
/// </summary>
/// <remarks>
/// Simpler than voiding an invoice or a credit note, and for the same reason the edit is: an order posted
/// nothing, so there is nothing to reverse. The header and its lines are soft-deleted through the audit
/// interceptor, a reason is mandatory, and <c>row_version</c> guards a stale copy.
/// </remarks>
public interface IPurchaseOrderDeleter
{
    Task<PurchaseOrderDeleted> DeleteAsync(long orderId, int expectedRowVersion, CancellationToken cancellationToken = default);
}
