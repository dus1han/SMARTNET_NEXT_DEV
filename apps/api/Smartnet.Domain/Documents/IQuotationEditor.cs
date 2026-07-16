namespace Smartnet.Domain.Documents;

/// <summary>One line of a quotation being edited; <see cref="Id"/> ties it to an existing line (null = new).</summary>
public sealed record EditQuotationLine(
    long? Id,
    long? ItemId,
    string? ItemCode,
    string? Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal DiscountPercent,
    decimal? Cost);

/// <summary>An edit to a quotation — its lines, discount, contact and validity. Reason-gated, version-guarded.</summary>
/// <param name="ExpectedRowVersion">The row_version the editor loaded; a stale one is a conflict.</param>
public sealed record EditQuotation(
    int ExpectedRowVersion,
    string? ContactPerson,
    string? Validity,
    decimal DocumentDiscountPercent,
    IReadOnlyList<EditQuotationLine> Lines);

/// <summary>What an edit returns — the new figures and the version it wrote.</summary>
public sealed record QuotationEdited(long Id, string Number, decimal Total, int VersionNo);

/// <summary>
/// Edits a quotation — versioned, reason-gated, concurrency-guarded (Phase 5, slice 5, legacy parity). The
/// mirror of <see cref="IInvoiceEditor"/> without the sale: no ledger, no stock. Re-runs the tax engine at
/// the quotation's snapshotted rate, reconciles lines in place, writes a new version. A legacy quotation is
/// adopted first; a <b>converted</b> quotation is spent and cannot be edited.
/// </summary>
public interface IQuotationEditor
{
    Task<QuotationEdited> EditAsync(long quotationId, EditQuotation request, CancellationToken cancellationToken = default);
}
