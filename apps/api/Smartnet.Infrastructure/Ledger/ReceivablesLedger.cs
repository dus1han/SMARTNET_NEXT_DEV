using Microsoft.EntityFrameworkCore;
using Smartnet.Domain.Ledger;
using Smartnet.Infrastructure.Persistence;

namespace Smartnet.Infrastructure.Ledger;

/// <inheritdoc cref="IReceivablesLedger"/>
public sealed class ReceivablesLedger : IReceivablesLedger
{
    private readonly SmartnetDbContext _db;

    public ReceivablesLedger(SmartnetDbContext db) => _db = db;

    public async Task<decimal> BalanceForCustomerAsync(long customerId, CancellationToken cancellationToken = default) =>
        // Summed in the database. An empty set is 0m, not null — SumAsync over decimal returns the
        // additive identity, which is exactly the balance of a customer who owes nothing.
        await _db.ReceivablesLedger
            // Suspending moves money off an invoice's outstanding, not off what the customer owes, so the
            // suspension pair is left out: the customer's balance is the same before and after.
            .Where(entry => entry.CustomerId == customerId
                && entry.Type != LedgerEntryType.Suspended
                && entry.Type != LedgerEntryType.Reinstated)
            .SumAsync(entry => entry.Amount, cancellationToken)
            .ConfigureAwait(false);
}
