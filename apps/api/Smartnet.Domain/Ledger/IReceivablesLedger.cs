namespace Smartnet.Domain.Ledger;

/// <summary>
/// Reads a customer's balance <b>back out of the ledger</b> — the derived figure, never a stored one.
/// </summary>
/// <remarks>
/// This is the read side of the B3 fix. Nowhere in the new app is a receivable balance stored; it is
/// always the sum of <see cref="LedgerEntry"/> rows. Credit-limit enforcement, the outstanding report
/// and the acceptance test all ask this the same way, so "what does a customer owe" has exactly one
/// answer and one code path.
/// </remarks>
public interface IReceivablesLedger
{
    /// <summary>What the customer owes: the sum of their ledger entries. Zero when they have none.</summary>
    Task<decimal> BalanceForCustomerAsync(long customerId, CancellationToken cancellationToken = default);
}
