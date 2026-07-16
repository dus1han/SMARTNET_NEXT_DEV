using Microsoft.EntityFrameworkCore;
using Smartnet.Domain.Ledger;
using Smartnet.Infrastructure.Persistence;

namespace Smartnet.Infrastructure.Ledger;

/// <inheritdoc cref="IPayablesLedger"/>
public sealed class PayablesLedger : IPayablesLedger
{
    private readonly SmartnetDbContext _db;

    public PayablesLedger(SmartnetDbContext db) => _db = db;

    public async Task<decimal> BalanceForSupplierAsync(long supplierId, CancellationToken cancellationToken = default) =>
        // Summed in the database. An empty set is 0m, not null — SumAsync over decimal returns the
        // additive identity, which is exactly the balance of a supplier we owe nothing.
        await _db.PayablesLedger
            .Where(entry => entry.SupplierId == supplierId)
            .SumAsync(entry => entry.Amount, cancellationToken)
            .ConfigureAwait(false);

    public async Task<decimal> OutstandingForInvoiceAsync(long supplierInvoiceId, CancellationToken cancellationToken = default) =>
        await _db.PayablesLedger
            .Where(entry => entry.SupplierInvoiceId == supplierInvoiceId)
            .SumAsync(entry => entry.Amount, cancellationToken)
            .ConfigureAwait(false);
}
