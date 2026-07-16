using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Smartnet.Domain.Documents;
using Smartnet.Domain.Ledger;
using Smartnet.Domain.MasterData;

using Smartnet.Infrastructure.Persistence;

namespace Smartnet.Infrastructure.Documents;

/// <inheritdoc cref="IInvoiceDeleter"/>
public sealed class InvoiceDeleter : IInvoiceDeleter
{
    private readonly SmartnetDbContext _db;
    private readonly TimeProvider _time;

    public InvoiceDeleter(SmartnetDbContext db, TimeProvider time)
    {
        _db = db;
        _time = time;
    }

    public async Task<InvoiceDeleted> DeleteAsync(long invoiceId, int expectedRowVersion, CancellationToken cancellationToken = default)
    {
        // One transaction: the reversals and the soft delete commit together or not at all.
        await using var transaction = await _db.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        var invoice = await _db.Invoices
            .Include(i => i.Lines)
            .FirstOrDefaultAsync(i => i.Id == invoiceId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Invoice {invoiceId} does not exist.");

        // Void a stale copy → concurrency conflict, same as the editor.
        _db.Entry(invoice).Property(i => i.RowVersion).OriginalValue = expectedRowVersion;

        await ReverseLedgerAsync(invoice, cancellationToken).ConfigureAwait(false);
        ReverseStock(invoice);

        // Soft-delete the header and every active line by setting deleted_at directly — an UPDATE, not a
        // Remove(). Remove() would put the invoice in EF's Deleted state, and the relationship fixup would
        // null the invoice_id on the reversal entries we just added before the interceptor could rewrite it
        // to a soft delete. Setting deleted_at is the interceptor's "WasDeleted" path: same soft delete, same
        // audit row with the reason, no cascade. (The interceptor stamps deleted_by and the real timestamp.)
        var now = _time.GetUtcNow().UtcDateTime;
        foreach (var line in invoice.Lines.Where(l => l.DeletedAt is null))
        {
            line.DeletedAt = now;
        }
        invoice.DeletedAt = now;

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false); // reversals + soft delete + audit + concurrency

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return new InvoiceDeleted(invoice.Id, invoice.Number);
    }

    /// <summary>
    /// Brings the invoice's receivable contribution back to zero with one compensating entry — the negation
    /// of everything it has posted (its charge, its cash settlement, any edit delta). The balance is derived
    /// from the ledger, so a single reversing entry undoes the invoice cleanly, and the history of what it
    /// did survives. Nothing to reverse (a cash invoice already nets to zero) posts nothing.
    /// </summary>
    private async Task ReverseLedgerAsync(Invoice invoice, CancellationToken cancellationToken)
    {
        var net = await _db.ReceivablesLedger
            .Where(e => e.InvoiceId == invoice.Id)
            .SumAsync(e => e.Amount, cancellationToken)
            .ConfigureAwait(false);

        if (net == 0m)
        {
            return;
        }

        _db.ReceivablesLedger.Add(new LedgerEntry
        {
            CustomerId = invoice.CustomerId,
            // A positive contribution (a live receivable) is reversed by a Credit; the rare negative one by
            // a Charge. Either way the amount is the negation, so the invoice's ledger sum becomes zero.
            Type = net > 0m ? LedgerEntryType.Credit : LedgerEntryType.Charge,
            Amount = -net,
            InvoiceId = invoice.Id,
            OccurredAt = invoice.Date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            Note = $"Invoice {invoice.Number} voided — ledger reversed",
        });
    }

    /// <summary>Returns the goods each item line issued — a stock receipt, the mirror of the create's issue.</summary>
    private void ReverseStock(Invoice invoice)
    {
        var occurredAt = invoice.Date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        foreach (var line in invoice.Lines.Where(l => l.DeletedAt is null && l.ItemId is not null))
        {
            _db.StockMovements.Add(new StockMovement
            {
                ItemId = line.ItemId!.Value,
                Type = StockMovementType.Receipt,
                Quantity = line.Quantity, // positive: the issued stock comes back
                OccurredAt = occurredAt,
                Reason = $"Invoice {invoice.Number} voided — stock returned",
            });
        }
    }
}
