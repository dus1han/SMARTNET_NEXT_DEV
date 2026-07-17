using Smartnet.Domain.Auditing;

namespace Smartnet.Domain.Ledger;

/// <summary>
/// One journal entry in the general ledger (GL slice 2) — a single money event, posted as balanced
/// debit/credit lines.
/// </summary>
/// <remarks>
/// Append-only, like the receivables and payables sub-ledgers: an entry is never edited or deleted, and a
/// mistake is corrected by a compensating entry. <see cref="SourceType"/>/<see cref="SourceId"/> tie it to the
/// event it posts (an invoice, a receipt, an expense…), which makes posting idempotent — one event, one entry.
/// </remarks>
public class GlEntry : IAuditable
{
    public long Id { get; set; }

    public long CompanyId { get; set; }

    /// <summary>The date the event occurred (the invoice/receipt/expense date), not when the row was written.</summary>
    public DateOnly Date { get; set; }

    /// <summary>What raised it — e.g. <c>Invoice</c>, <c>CustomerReceipt</c>, <c>Expense</c>, <c>Backfill</c>.</summary>
    public string SourceType { get; set; } = null!;

    /// <summary>The id of the source event, so an event posts exactly once.</summary>
    public long SourceId { get; set; }

    public string? Description { get; set; }

    public ICollection<GlLine> Lines { get; set; } = new List<GlLine>();

    public long? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public long? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public long? DeletedBy { get; set; }
    public DateTime? DeletedAt { get; set; }
    public int RowVersion { get; set; }
}

/// <summary>One debit/credit line of a <see cref="GlEntry"/>. Exactly one of debit/credit is non-zero.</summary>
public class GlLine : IAuditable
{
    public long Id { get; set; }

    public long? GlEntryId { get; set; }

    public long AccountId { get; set; }

    /// <summary>The debit amount (increases assets/expenses).</summary>
    public decimal Debit { get; set; }

    /// <summary>The credit amount (increases liabilities/income/equity).</summary>
    public decimal Credit { get; set; }

    public GlEntry Entry { get; set; } = null!;

    public long? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public long? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public long? DeletedBy { get; set; }
    public DateTime? DeletedAt { get; set; }
    public int RowVersion { get; set; }
}
