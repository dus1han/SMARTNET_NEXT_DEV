namespace Smartnet.Domain.Documents;

/// <summary>What a delete returns — enough to confirm and route back to the list.</summary>
public sealed record InvoiceDeleted(long Id, string Number);

/// <summary>
/// Deletes (voids) an issued invoice — soft, recoverable, attributable (Phase 5, slice 5).
/// </summary>
/// <remarks>
/// Nothing is hard-deleted: the header and its lines are soft-deleted through the audit interceptor, so the
/// row survives, stays attributable and can be restored. A reason is mandatory. The invoice's effect on the
/// world is reversed <b>through new entries, never by erasing history</b>: a compensating ledger entry
/// brings its receivable contribution back to zero (never <c>UPDATE … SET balance = 0</c>), and a stock
/// <b>receipt</b> returns the goods each item line issued. Guarded by <c>row_version</c>, so voiding a stale
/// copy is rejected.
/// <para>
/// The broken legacy <c>delCN</c> (which threw on wrong column names) is not ported; this is its
/// replacement, done correctly.
/// </para>
/// </remarks>
public interface IInvoiceDeleter
{
    /// <param name="expectedRowVersion">The row_version the caller loaded; a stale one is a conflict.</param>
    Task<InvoiceDeleted> DeleteAsync(long invoiceId, int expectedRowVersion, CancellationToken cancellationToken = default);
}
