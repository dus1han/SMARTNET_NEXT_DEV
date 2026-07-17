using Smartnet.Domain.Auditing;

namespace Smartnet.Domain.Documents;

/// <summary>
/// One allocation of a <see cref="SupplierPayment"/> against a single supplier invoice (Phase 7).
/// </summary>
/// <remarks>
/// Each posts a payables-ledger <c>Payment</c> entry for its <see cref="SupplierInvoiceId"/> and dual-writes
/// a legacy <c>supplier_inv_pay</c> row (so the legacy detail reads one payment row per invoice, as before).
/// </remarks>
public class SupplierPaymentAllocation : IAuditable
{
    public long Id { get; set; }

    /// <summary>The parent payment's surrogate id.</summary>
    public long? SupplierPaymentId { get; set; }

    /// <summary>The supplier invoice this allocation settles, by surrogate id (a new or a legacy <c>supplier_invoice.id</c>).</summary>
    public long SupplierInvoiceId { get; set; }

    /// <summary>How much of the payment is applied to that invoice.</summary>
    public decimal Amount { get; set; }

    public SupplierPayment Payment { get; set; } = null!;

    public long? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public long? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public long? DeletedBy { get; set; }
    public DateTime? DeletedAt { get; set; }
    public int RowVersion { get; set; }
}
