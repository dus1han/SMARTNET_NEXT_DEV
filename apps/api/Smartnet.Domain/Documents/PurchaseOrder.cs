using Smartnet.Domain.Auditing;

namespace Smartnet.Domain.Documents;

/// <summary>
/// A purchase order — the new-side aggregate, mapped onto the adopted legacy <c>po_h</c> table
/// (Phase 6, slice 1).
/// </summary>
/// <remarks>
/// The supply-side counterpart of a <see cref="Quotation"/>: like a quotation it <b>charges nothing and
/// issues nothing</b> — it is an <i>order</i>, not a receipt. There is no ledger entry (a PO is not yet a
/// payable — the supplier invoice is, Phase 6 slice 2) and <b>no stock movement</b> (goods are received,
/// in partial quantities, by a GRN that is deferred to a later phase — see PHASE-6-PLAN §B′). What it
/// carries is a <see cref="SupplierId"/> rather than a customer, and item lines that hold their
/// <see cref="PurchaseOrderLine.ItemId"/> and cost so the future GRN can receive against them.
///
/// <para><b>Additive adoption</b>, exactly as invoices and quotations were: the migration adds a primary
/// key (<c>po_h</c> has none), the audit columns, and new <c>decimal</c>/<c>date</c> columns beside the
/// legacy <c>varchar</c> ones, which the save writes alongside so the surviving legacy readers (SearchPO's
/// list, the PO reprint/email) still see a complete row. The tax rate is resolved at the PO's date and
/// snapshotted onto the header (the <c>one-vat-rate-per-document</c> decision), fixing the legacy
/// <c>CURDATE()</c> resolution so a reprint reproduces the figures it was issued with.</para>
///
/// <para>Unlike an invoice, a PO has <b>no cash/credit type and no credit-limit gate</b> — it is a
/// supplier order, not a customer sale. The legacy <c>po_h</c> has no <c>contactperson</c>, <c>it</c>,
/// <c>discountper</c> or <c>beforedisctot</c> columns (its header is thinner than an invoice's), and its
/// VAT columns are <c>vatty</c>/<c>vatpercent</c>/<c>nonvattotal</c> — so the legacy shadow set differs.</para>
/// </remarks>
public class PurchaseOrder : IAuditable, ISoftDeletable
{
    /// <summary>Added by the migration; <c>po_h</c> has no key of any kind.</summary>
    public long Id { get; set; }

    /// <summary>The PO number, allocated transactionally from <c>document_series</c> at save (legacy <c>po_no</c>).</summary>
    public string Number { get; set; } = null!;

    /// <summary>
    /// The trading entity. Nullable only because it maps to the <c>company_id</c> the multi-company
    /// migration added as nullable; a new PO always sets it.
    /// </summary>
    public long? CompanyId { get; set; }

    /// <summary>The supplier, by surrogate key — a real reference, not the legacy <c>supplier</c> code.</summary>
    public long SupplierId { get; set; }

    /// <summary>The document date — the date the tax rate is resolved as of. Typed, not the legacy varchar.</summary>
    public DateOnly Date { get; set; }

    /// <summary>The user who raised it, by id — not the legacy <c>preparedby</c> name string.</summary>
    public long? PreparedBy { get; set; }

    // --- Money, in decimal, computed by the one tax engine and stored as resolved ---------------

    /// <summary>Σ of the line gross amounts, before the document discount.</summary>
    public decimal Subtotal { get; set; }

    /// <summary>The document-level discount percentage.</summary>
    public decimal DiscountPercent { get; set; }

    public decimal DiscountAmount { get; set; }

    /// <summary>The taxable amount — subtotal less discount (legacy <c>nonvattotal</c>).</summary>
    public decimal NetTotal { get; set; }

    /// <summary>The rate the document was ordered at, or null when the company is not VAT-registered.</summary>
    public long? TaxRateId { get; set; }

    /// <summary>The rate percentage, snapshotted at save (legacy <c>vatpercent</c>) — never re-resolved.</summary>
    public decimal TaxRatePercentage { get; set; }

    public decimal TaxAmount { get; set; }

    /// <summary>The VAT-inclusive grand total ordered (legacy <c>totamount</c>).</summary>
    public decimal Total { get; set; }

    /// <summary>Σ of the line costs — the cost basis carried for the future goods receipt.</summary>
    public decimal Cost { get; set; }

    /// <summary>
    /// <c>new</c> for POs this app raises; existing legacy rows are <c>legacy</c> and are never read as a
    /// <see cref="PurchaseOrder"/> (a query filter excludes them).
    /// </summary>
    public string DataOrigin { get; set; } = "new";

    public ICollection<PurchaseOrderLine> Lines { get; set; } = new List<PurchaseOrderLine>();

    public long? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public long? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public long? DeletedBy { get; set; }
    public DateTime? DeletedAt { get; set; }
    public int RowVersion { get; set; }
}
