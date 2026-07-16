using Smartnet.Domain.Auditing;

namespace Smartnet.Domain.Documents;

/// <summary>
/// A supplier invoice — the new-side aggregate, mapped onto the adopted legacy <c>supplier_invoice</c>
/// table (Phase 6, slice 2). An accounts-payable record: what we owe a supplier, and against which their
/// payments settle.
/// </summary>
/// <remarks>
/// <b>Header-only, by decision (2026-07-16).</b> The legacy supplier invoice has no line-item table and no
/// tax engine — the user types the <see cref="Amount"/>, the pre-VAT <see cref="NetTotal"/> and the VAT
/// <see cref="TaxRatePercentage"/> — and the rebuild keeps that shape: goods enter stock via a purchase
/// order or a stock adjustment, never through the invoice, so a supplier invoice moves <b>no stock</b> and
/// carries <b>no lines</b>. What it does own is the <b>payable</b>: creating it posts a
/// <see cref="Smartnet.Domain.Ledger.PayablesLedgerEntryType.Purchase"/> entry, and payments post
/// <see cref="Smartnet.Domain.Ledger.PayablesLedgerEntryType.Payment"/> entries, so the outstanding is
/// derived (never a stored, mutated column) and <b>partial payments work</b> — both of which the legacy
/// binary <c>paymentstat</c> flag could not do.
///
/// <para><b>Additive adoption.</b> Unlike <c>invoice_h</c>/<c>po_h</c>, <c>supplier_invoice</c> already had
/// an <c>int</c> <c>id</c> — but under a non-unique <c>KEY</c>, not a primary key (Finding 6). The
/// migration promotes it to a real <c>bigint</c> primary key; the typed <c>decimal</c>/<c>date</c> columns
/// are added beside the legacy <c>varchar</c> ones, which the save keeps in step (including the legacy
/// <c>paymentstat</c> flag and a dual-written <c>supplier_inv_pay</c> row) so the surviving legacy readers
/// (the supplier reports) keep working.</para>
///
/// <para>There is <b>no system-allocated number</b>: a supplier invoice is identified by its surrogate id
/// and displays the supplier's own reference (<see cref="SupplierReference"/>, the legacy <c>invno</c>) —
/// the number the supplier put on it, not one we mint.</para>
/// </remarks>
public class SupplierInvoice : IAuditable, ISoftDeletable
{
    /// <summary>The surrogate key — the legacy <c>int id</c> promoted to a <c>bigint</c> primary key.</summary>
    public long Id { get; set; }

    /// <summary>The supplier's own invoice number (legacy <c>invno</c>) — free text, theirs, not ours.</summary>
    public string? SupplierReference { get; set; }

    /// <summary>
    /// The trading entity. Nullable only because it maps to the <c>company_id</c> the multi-company
    /// migration added as nullable; a new supplier invoice always sets it.
    /// </summary>
    public long? CompanyId { get; set; }

    /// <summary>The supplier, by surrogate key — a real reference, not the legacy <c>supcode</c> string.</summary>
    public long SupplierId { get; set; }

    /// <summary>The invoice date (the supplier's), typed — not the legacy <c>invdate</c> varchar.</summary>
    public DateOnly Date { get; set; }

    // --- Money, in decimal, as entered (no line-item tax engine — the supplier's figures) -------

    /// <summary>The pre-VAT amount (legacy <c>novattotal</c>).</summary>
    public decimal NetTotal { get; set; }

    /// <summary>The VAT percentage on the invoice (legacy <c>vper</c>).</summary>
    public decimal TaxRatePercentage { get; set; }

    /// <summary>The VAT-inclusive total we owe for it (legacy <c>amount</c>).</summary>
    public decimal Amount { get; set; }

    /// <summary>The VAT portion — derived, not stored separately (<see cref="Amount"/> − <see cref="NetTotal"/>).</summary>
    public decimal TaxAmount => Amount - NetTotal;

    /// <summary>
    /// <c>new</c> for supplier invoices this app raises; existing legacy rows are <c>legacy</c> and are
    /// never read as a <see cref="SupplierInvoice"/> (a query filter excludes them).
    /// </summary>
    public string DataOrigin { get; set; } = "new";

    public long? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public long? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public long? DeletedBy { get; set; }
    public DateTime? DeletedAt { get; set; }
    public int RowVersion { get; set; }
}
