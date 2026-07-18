namespace Smartnet.Domain.Documents;

/// <summary>What a credit-note void returns — enough to confirm and route back to the list.</summary>
public sealed record CreditNoteDeleted(long Id, string Number);

/// <summary>
/// Voids a credit note — soft, recoverable, attributable.
/// </summary>
/// <remarks>
/// The mirror of <see cref="IInvoiceDeleter"/>, and built the same way: nothing is hard-deleted, a reason is
/// mandatory, the row is guarded by <c>row_version</c>, and the note's effect on the world is undone
/// <b>through new entries, never by erasing history</b>.
///
/// <para>What a credit note did is the opposite of an invoice, so the reversal is too. Its ledger entry
/// <i>credited</i> the customer (reducing what they owe), so voiding it posts a compensating
/// <see cref="Ledger.LedgerEntryType.Charge"/> that puts the amount back. Where the note returned goods to
/// stock, voiding issues them out again.</para>
///
/// <para><b>A legacy note posted nothing to reverse.</b> Credit notes raised by the old system adjusted
/// <c>invoice_h.balance</c> directly and never wrote to this system's ledger or stock. Voiding one is
/// therefore a soft delete alone — posting a "reversal" for entries that do not exist would invent a
/// receivable out of nothing. The distinction is made on what the note actually posted, not on a flag.</para>
/// </remarks>
public interface ICreditNoteDeleter
{
    /// <param name="expectedRowVersion">The row_version the caller loaded; a stale one is a conflict.</param>
    Task<CreditNoteDeleted> DeleteAsync(long creditNoteId, int expectedRowVersion, CancellationToken cancellationToken = default);
}
