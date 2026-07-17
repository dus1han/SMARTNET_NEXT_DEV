using Smartnet.Domain.Auditing;

namespace Smartnet.Domain.Documents;

/// <summary>
/// One allocation of a <see cref="CustomerReceipt"/> against a single invoice (Phase 7, slice 1).
/// </summary>
/// <remarks>
/// The line that makes a receipt richer than the legacy one-payment-one-invoice: a receipt carries one of
/// these per invoice it settles. Each posts a receivables-ledger <c>Payment</c> entry for its
/// <see cref="InvoiceId"/> and dual-writes a legacy <c>payments</c> row (so the legacy detail reads the same,
/// one payment row per invoice, exactly as before).
/// </remarks>
public class ReceiptAllocation : IAuditable
{
    public long Id { get; set; }

    /// <summary>The parent receipt's surrogate id.</summary>
    public long? CustomerReceiptId { get; set; }

    /// <summary>The invoice this allocation settles, by surrogate id (a new or a legacy invoice's <c>invoice_h.id</c>).</summary>
    public long InvoiceId { get; set; }

    /// <summary>How much of the receipt is applied to that invoice.</summary>
    public decimal Amount { get; set; }

    public CustomerReceipt Receipt { get; set; } = null!;

    public long? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public long? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public long? DeletedBy { get; set; }
    public DateTime? DeletedAt { get; set; }
    public int RowVersion { get; set; }
}
