namespace Smartnet.Domain.Documents;

/// <summary>
/// The customer code on a legacy invoice is not in the customer master, so it cannot be adopted (the new
/// model keys the customer by surrogate id, and the ledger by that id).
/// </summary>
public sealed class LegacyDocumentNotAdoptableException(string number, string detail)
    : Exception($"Invoice {number} cannot be adopted from the legacy system: {detail}")
{
    public string Number { get; } = number;
}

/// <summary>
/// Adopts a legacy invoice into the new model — the bridge that lets the new app edit and void documents it
/// did not raise (Phase 5, slice 5b, legacy parity).
/// </summary>
/// <remarks>
/// A legacy invoice shares the <c>invoice_h</c> table but its typed columns are empty placeholders — the
/// real figures live in the legacy <c>varchar</c> columns. Adoption reads those, resolves the customer and
/// item codes to surrogate ids, parses the stored date and VAT rate, <b>recomputes the money through the
/// decimal tax engine from the lines</b> (the figure the new app stands behind — confirmed 2026-07-16),
/// links the lines, flips <c>data_origin</c> to <c>new</c>, and writes a version-1 "as imported" snapshot.
/// The invoice keeps its number and its <c>OPENING_BALANCE</c> ledger entry; adoption changes what the
/// document <i>is</i>, not what is owed.
///
/// <para>Idempotent: a document already <c>new</c> is returned unchanged. Designed to run both <b>on demand</b>
/// (the first time a legacy invoice is edited or voided) and <b>in bulk</b> (the go-live migration adopts
/// every legacy document at once, when the old app stops writing these tables).</para>
/// </remarks>
public interface ILegacyInvoiceAdopter
{
    /// <summary>
    /// Materialises a loaded, tracked legacy invoice in place — sets its typed columns and lines from the
    /// legacy data and writes the version-1 snapshot — <b>inside the caller's transaction</b>. Saves the
    /// change (so the concurrency check fires against the row_version the caller set). A no-op if the
    /// invoice is already <c>new</c>.
    /// </summary>
    Task MaterialiseInCurrentTransactionAsync(Invoice invoice, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adopts a legacy invoice by id in its own transaction — the entry point for a bulk go-live migration
    /// or a stand-alone adopt. Returns the adopted invoice's id, or throws if it cannot be adopted.
    /// </summary>
    Task<long> AdoptAsync(long invoiceId, CancellationToken cancellationToken = default);
}
