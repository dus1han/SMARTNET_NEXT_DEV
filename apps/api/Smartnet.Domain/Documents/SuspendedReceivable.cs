using Smartnet.Domain.Auditing;

namespace Smartnet.Domain.Documents;

/// <summary>Where a suspended amount is now.</summary>
public enum SuspendedReceivableStatus
{
    /// <summary>On the suspended account — off the invoice's outstanding.</summary>
    Suspended,

    /// <summary>Brought back onto the invoice, to be collected the ordinary way.</summary>
    Reinstated,

    /// <summary>Brought back and paid in the same act — a receipt was recorded against it.</summary>
    Settled,
}

/// <summary>
/// One amount moved from an invoice's outstanding to the suspended account — the debt a customer still
/// owes but which is not being chased, so it shows nowhere an outstanding figure does.
/// </summary>
/// <remarks>
/// <para><b>The ledger is the truth, this is the register.</b> Suspending posts a
/// <see cref="Ledger.LedgerEntryType.Suspended"/> entry (and reinstating its reverse), so every derived
/// outstanding drops by the amount with no filter to remember anywhere. This row is what the suspended
/// account screen lists, and what reinstating reverses.</para>
///
/// <para><b>New and legacy invoices alike</b> — both live in <c>invoice_h</c> and both carry their
/// outstanding in the receivables ledger.</para>
///
/// <para>Never deleted: once reinstated or settled it stays, so the account's history is readable.</para>
/// </remarks>
public class SuspendedReceivable : IAuditable
{
    public long Id { get; set; }

    public long CompanyId { get; set; }

    public long CustomerId { get; set; }

    /// <summary>The invoice (<c>invoice_h.id</c>, new or legacy) the amount was taken off.</summary>
    public long InvoiceId { get; set; }

    /// <summary>The invoice number as it stood, so the list reads without a join.</summary>
    public string InvoiceNumber { get; set; } = null!;

    public decimal Amount { get; set; }

    /// <summary>The date it was suspended — the ledger entry's date.</summary>
    public DateOnly SuspendedOn { get; set; }

    /// <summary>Why — required, because this hides a debt.</summary>
    public string Reason { get; set; } = null!;

    public SuspendedReceivableStatus Status { get; set; }

    public DateTime? ReinstatedAt { get; set; }
    public long? ReinstatedBy { get; set; }
    public string? ReinstateNote { get; set; }

    /// <summary>The receipt that settled it, when it was brought back by a payment.</summary>
    public long? ReceiptId { get; set; }

    public long? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public long? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public long? DeletedBy { get; set; }
    public DateTime? DeletedAt { get; set; }
    public int RowVersion { get; set; }
}
