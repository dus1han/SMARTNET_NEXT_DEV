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
            .Where(entry => entry.CustomerId == customerId)
            .SumAsync(entry => entry.Amount, cancellationToken)
            .ConfigureAwait(false);
}
