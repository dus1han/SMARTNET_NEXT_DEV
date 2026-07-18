using Microsoft.EntityFrameworkCore;
using Smartnet.Domain.Documents;
using Smartnet.Domain.Ledger;
using Smartnet.Domain.MasterData;
using Smartnet.Infrastructure.Persistence;

namespace Smartnet.Infrastructure.Documents;

/// <inheritdoc cref="ICreditNoteDeleter"/>
public sealed class CreditNoteDeleter : ICreditNoteDeleter
{
    private readonly SmartnetDbContext _db;
    private readonly TimeProvider _time;

    public CreditNoteDeleter(SmartnetDbContext db, TimeProvider time)
    {
        _db = db;
        _time = time;
    }

    public async Task<CreditNoteDeleted> DeleteAsync(
        long creditNoteId,
        int expectedRowVersion,
        CancellationToken cancellationToken = default)
    {
        // One transaction: the reversals and the soft delete commit together or not at all.
        await using var transaction = await _db.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        // IgnoreQueryFilters so a legacy note loads too — it is voidable, it simply has nothing to reverse.
        var note = await _db.CreditNotes
            .IgnoreQueryFilters()
            .Include(c => c.Lines)
            .FirstOrDefaultAsync(c => c.Id == creditNoteId && c.DeletedAt == null, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Credit note {creditNoteId} does not exist.");

        // Void a stale copy → concurrency conflict, same as every other document.
        _db.Entry(note).Property(c => c.RowVersion).OriginalValue = expectedRowVersion;

        await ReverseLedgerAsync(note, cancellationToken).ConfigureAwait(false);
        ReverseStock(note);

        // Soft-delete by setting deleted_at directly rather than Remove(): Remove() would put the note in
        // EF's Deleted state and the relationship fixup would null the ids on the reversal entries just
        // added, before the interceptor could rewrite it to a soft delete. This is the interceptor's
        // "WasDeleted" path — same soft delete, same audit row carrying the reason, no cascade.
        var now = _time.GetUtcNow().UtcDateTime;

        foreach (var line in note.Lines.Where(l => l.DeletedAt is null))
        {
            line.DeletedAt = now;
        }

        note.DeletedAt = now;

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return new CreditNoteDeleted(note.Id, note.Number);
    }

    /// <summary>
    /// Puts back what the note credited.
    /// </summary>
    /// <remarks>
    /// The amount is read from the ledger rather than assumed from the note's total. The two agree for a
    /// note this system raised, but a legacy note has <b>no ledger entry at all</b> — it adjusted
    /// <c>invoice_h.balance</c> in the old app — and posting its total as a "reversal" would charge a
    /// customer for a receivable this system never recorded.
    ///
    /// <para>A credit note's entry is filed against its <i>parent invoice</i>, not against itself
    /// (<see cref="LedgerEntry.InvoiceId"/> is the invoice's id), so its own contribution is identified by
    /// the note it was written with. That is the only link the schema offers today; a
    /// <c>credit_note_id</c> column would make it structural.</para>
    /// </remarks>
    private async Task ReverseLedgerAsync(CreditNote note, CancellationToken cancellationToken)
    {
        var marker = $"Credit note {note.Number}";

        var posted = await _db.ReceivablesLedger
            .Where(e => e.InvoiceId == note.InvoiceId && e.Note == marker)
            .SumAsync(e => e.Amount, cancellationToken)
            .ConfigureAwait(false);

        // Nothing posted — a legacy note, or one whose entry has already been reversed. Either way there
        // is nothing to undo, and inventing an entry here would be inventing money.
        if (posted == 0m)
        {
            return;
        }

        _db.ReceivablesLedger.Add(new LedgerEntry
        {
            CustomerId = note.CustomerId,
            // The note credited (a negative contribution), so the reversal charges. The sign is the
            // negation either way, so the pair nets to zero.
            Type = posted < 0m ? LedgerEntryType.Charge : LedgerEntryType.Credit,
            Amount = -posted,
            InvoiceId = note.InvoiceId,
            OccurredAt = note.Date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            Note = $"Credit note {note.Number} voided — ledger reversed",
        });
    }

    /// <summary>
    /// Issues out again whatever the note returned to stock — the mirror of the create's receipt.
    /// </summary>
    /// <remarks>
    /// Only when the note actually returned goods. A pure price adjustment moved no stock, so voiding it
    /// must move none either; issuing stock for a note that never received any would take goods off the
    /// shelf that were never put back.
    /// </remarks>
    private void ReverseStock(CreditNote note)
    {
        if (!note.ReturnsStock)
        {
            return;
        }

        var occurredAt = note.Date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        foreach (var line in note.Lines.Where(l => l.DeletedAt is null && l.ItemId is not null))
        {
            _db.StockMovements.Add(new StockMovement
            {
                ItemId = line.ItemId!.Value,
                Type = StockMovementType.Issue,
                Quantity = -line.Quantity, // negative: the returned stock goes back out
                OccurredAt = occurredAt,
                Reason = $"Credit note {note.Number} voided — stock reissued",
            });
        }
    }
}
