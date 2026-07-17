using Smartnet.Domain.Auditing;

namespace Smartnet.Domain.Documents;

/// <summary>
/// A supplier payment — money paid to a supplier, allocated across one or more open supplier invoices
/// (Phase 7, slice 1 follow-up). The payables mirror of <see cref="CustomerReceipt"/>.
/// </summary>
/// <remarks>
/// The legacy app paid one supplier invoice at a time (<c>supplier_inv_pay</c>, amountless) and flipped a
/// binary <c>paymentstat</c>. Here a payment is <b>allocated across several invoices</b>: each allocation
/// posts a <see cref="Smartnet.Domain.Ledger.PayablesLedgerEntryType.Payment"/> entry to the payables ledger
/// — the source of truth, from which every supplier balance is derived — and <b>dual-writes the legacy
/// shadow</b> (a <c>supplier_inv_pay</c> row per invoice, and <c>paymentstat='Paid'</c> once an invoice's
/// derived outstanding reaches zero) so the surviving legacy supplier-payment report keeps reading. An
/// <see cref="IdempotencyKey"/> makes a double-submit return the same payment rather than pay twice, and the
/// whole save is one transaction. It settles new and adopted-legacy supplier invoices alike.
/// </remarks>
public class SupplierPayment : IAuditable, ISoftDeletable
{
    public long Id { get; set; }

    /// <summary>The trading entity. Nullable to match the multi-company columns; a new payment always sets it.</summary>
    public long? CompanyId { get; set; }

    /// <summary>The supplier the money was paid to, by surrogate key.</summary>
    public long SupplierId { get; set; }

    /// <summary>The date the payment was made.</summary>
    public DateOnly Date { get; set; }

    /// <summary>The total paid — the sum of the allocations.</summary>
    public decimal Amount { get; set; }

    /// <summary>How it was paid — Cash, Bank, Cheque, Online (the legacy <c>pay_method</c>).</summary>
    public string? Method { get; set; }

    /// <summary>A reference — cheque number, transfer ref (the legacy <c>referenceno</c>).</summary>
    public string? Reference { get; set; }

    /// <summary>
    /// A client-supplied key that makes the create idempotent: a resubmit with the same key returns the
    /// existing payment rather than paying the supplier twice.
    /// </summary>
    public string IdempotencyKey { get; set; } = null!;

    /// <summary><c>new</c> for payments this app raises; there is no legacy payment header (the shadow is <c>supplier_inv_pay</c>).</summary>
    public string DataOrigin { get; set; } = "new";

    public ICollection<SupplierPaymentAllocation> Allocations { get; set; } = new List<SupplierPaymentAllocation>();

    public long? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public long? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public long? DeletedBy { get; set; }
    public DateTime? DeletedAt { get; set; }
    public int RowVersion { get; set; }
}
