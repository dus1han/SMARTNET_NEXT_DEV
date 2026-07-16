namespace Smartnet.Domain.Documents;

/// <summary>
/// One line of an invoice being edited. <see cref="Id"/> ties it to the line that already exists, so the
/// edit is reconciled non-destructively — an existing line is updated in place, a line with no id is added,
/// and an existing line the edit no longer carries is soft-deleted. Null id for a newly added line.
/// </summary>
public sealed record EditInvoiceLine(
    long? Id,
    long? ItemId,
    string? ItemCode,
    string? Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal DiscountPercent,
    decimal? Cost);

/// <summary>
/// An edit to an issued invoice — the whole of its editable state, posted at once.
/// </summary>
/// <param name="ExpectedRowVersion">
/// The <c>row_version</c> the editor loaded. The save fails loudly if the invoice has changed since
/// (someone else edited it), rather than silently overwriting their change — the legacy last-write-wins bug.
/// </param>
/// <remarks>
/// What an edit may change: the lines, the whole-document discount, the PO and the contact. What it may
/// <b>not</b>: the company, customer, type or date — those are the invoice's identity, and changing the date
/// would re-resolve the tax rate. The rate is therefore held at the <b>snapshot</b> the invoice was issued
/// under, and the tax engine re-run against it, so an edit corrects figures without silently re-rating.
/// </remarks>
public sealed record EditInvoice(
    int ExpectedRowVersion,
    string? PurchaseOrderNo,
    string? ContactPerson,
    decimal DocumentDiscountPercent,
    IReadOnlyList<EditInvoiceLine> Lines);

/// <summary>What the caller gets back after an edit — the new figures and the version it wrote.</summary>
public sealed record InvoiceEdited(long Id, string Number, decimal Total, decimal Outstanding, int VersionNo);

/// <summary>
/// Edits an issued invoice — versioned, reason-gated, concurrency-guarded (Phase 5, slice 5).
/// </summary>
/// <remarks>
/// This is where the legacy edit's three bugs are closed at once. The legacy edit deleted and re-inserted
/// every line, <b>reset the balance</b> (wiping partial-payment history) and, for a cash invoice, inserted a
/// <b>second</b> payment row (double-booking). Here, in one transaction: the tax engine is re-run at the
/// invoice's snapshotted rate; the lines are reconciled <b>in place</b> (update / add / soft-delete, never
/// delete-and-reinsert); a <b>new</b> <c>document_versions</c> snapshot is written with the reason, so the
/// prior version still prints as it was; the save is guarded by <c>row_version</c> so a concurrent edit is
/// rejected; and, when the total changes, the ledger is adjusted by a single compensating <c>CHARGE</c>
/// entry (plus a settlement delta for a cash invoice) — never by resetting the balance.
/// </remarks>
public interface IInvoiceEditor
{
    /// <summary>Applies the edit and returns the new figures. Throws on a concurrency conflict.</summary>
    Task<InvoiceEdited> EditAsync(long invoiceId, EditInvoice request, CancellationToken cancellationToken = default);
}
