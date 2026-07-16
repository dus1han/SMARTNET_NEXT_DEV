namespace Smartnet.Domain.Ledger;

/// <summary>
/// Reads a supplier's balance <b>back out of the ledger</b> — the derived figure, never a stored one. The
/// supply-side mirror of <see cref="IReceivablesLedger"/> (Phase 6, slice 2).
/// </summary>
/// <remarks>
/// Nowhere in the new app is a payable balance stored; it is always the sum of
/// <see cref="PayablesLedgerEntry"/> rows. So "what do we owe this supplier" — and, per invoice, "is it
/// settled yet" — have exactly one answer and one code path, and partial payments simply accumulate.
/// </remarks>
public interface IPayablesLedger
{
    /// <summary>What we owe the supplier: the sum of their ledger entries. Zero when they have none.</summary>
    Task<decimal> BalanceForSupplierAsync(long supplierId, CancellationToken cancellationToken = default);

    /// <summary>
    /// What is still outstanding on a single supplier invoice — the sum of its entries (its purchase less
    /// any payments against it). Zero means fully paid.
    /// </summary>
    Task<decimal> OutstandingForInvoiceAsync(long supplierInvoiceId, CancellationToken cancellationToken = default);
}
