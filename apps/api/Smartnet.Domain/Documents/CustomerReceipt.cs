using Smartnet.Domain.Auditing;

namespace Smartnet.Domain.Documents;

/// <summary>
/// A customer receipt — money received from a customer, allocated across one or more open invoices
/// (Phase 7, slice 1). The receivables counterpart of a supplier payment.
/// </summary>
/// <remarks>
/// The legacy app took one payment against one invoice and settled it by <c>UPDATE invoice_h SET balance =
/// balance - amount</c> — non-transactional, no idempotency (the Finding-1 duplicate mechanism), the balance
/// mutated in place (B2/B3). Here a receipt is <b>allocated across several invoices</b>: each allocation posts
/// a <see cref="Smartnet.Domain.Ledger.LedgerEntryType.Payment"/> entry to the receivables ledger — the new
/// source of truth, from which every balance is derived — and <b>dual-writes the legacy shadow</b> (a legacy
/// <c>payments</c> row and the <c>invoice_h.balance</c>) so the still-live legacy readers (the outstanding
/// report) stay correct and its detail reads the same. An <see cref="IdempotencyKey"/> makes a double-submit
/// return the same receipt rather than pay twice (Finding 1, closed), and the whole save is one transaction.
///
/// <para>A genuinely new concept, so <c>customer_receipts</c>/<c>receipt_allocations</c> are new tables — the
/// legacy <c>payments</c> table is the <i>shadow</i> it dual-writes, not the source of truth. It settles new
/// and legacy invoices alike (a legacy invoice's outstanding is its seeded opening balance plus any payments).</para>
/// </remarks>
public class CustomerReceipt : IAuditable, ISoftDeletable
{
    public long Id { get; set; }

    /// <summary>The trading entity. Nullable to match the multi-company columns; a new receipt always sets it.</summary>
    public long? CompanyId { get; set; }

    /// <summary>The customer the money came from, by surrogate key.</summary>
    public long CustomerId { get; set; }

    /// <summary>The date the money was received.</summary>
    public DateOnly Date { get; set; }

    /// <summary>The total received — the sum of the allocations.</summary>
    public decimal Amount { get; set; }

    /// <summary>How it was received — Cash, Cheque or Online (the legacy <c>paym</c>).</summary>
    public string? Method { get; set; }

    /// <summary>A reference — cheque number, transfer ref (the legacy <c>payref</c>).</summary>
    public string? Reference { get; set; }

    /// <summary>
    /// A client-supplied key that makes the create idempotent: a resubmit with the same key returns the
    /// existing receipt rather than taking the money twice. This is the fix for Finding 1.
    /// </summary>
    public string IdempotencyKey { get; set; } = null!;

    /// <summary><c>new</c> for receipts this app raises; there is no legacy receipt (the legacy shadow is <c>payments</c>).</summary>
    public string DataOrigin { get; set; } = "new";

    public ICollection<ReceiptAllocation> Allocations { get; set; } = new List<ReceiptAllocation>();

    public long? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public long? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public long? DeletedBy { get; set; }
    public DateTime? DeletedAt { get; set; }
    public int RowVersion { get; set; }
}
