namespace Smartnet.Domain.Documents;

/// <summary>What a quotation void returns.</summary>
public sealed record QuotationDeleted(long Id, string Number);

/// <summary>
/// Voids a quotation — soft, recoverable, attributable (Phase 5, slice 5, legacy parity). Simpler than an
/// invoice void: a quotation has no ledger or stock to reverse, so it is just a reason-gated,
/// version-guarded soft delete of the header and its lines. A legacy quotation is adopted first.
/// </summary>
public interface IQuotationDeleter
{
    Task<QuotationDeleted> DeleteAsync(long quotationId, int expectedRowVersion, CancellationToken cancellationToken = default);
}

/// <summary>
/// Adopts a legacy quotation into the new model on first edit or void — the mirror of
/// <see cref="ILegacyInvoiceAdopter"/>, without a ledger. Materialises the typed columns and lines from the
/// legacy <c>varchar</c> data, recomputes the money through the decimal engine, and writes a version-1 "as
/// imported" snapshot. Idempotent; reusable for the go-live bulk migration.
/// </summary>
public interface ILegacyQuotationAdopter
{
    Task MaterialiseInCurrentTransactionAsync(Quotation quotation, CancellationToken cancellationToken = default);

    Task<long> AdoptAsync(long quotationId, CancellationToken cancellationToken = default);
}
