using Smartnet.Domain.Auditing;

namespace Smartnet.Domain.Documents;

/// <summary>How an invoice is settled — the legacy <c>invtype</c>.</summary>
public enum InvoiceType
{
    /// <summary>Settled at issue. The save posts a charge and an equal settling payment to the ledger.</summary>
    Cash,

    /// <summary>Owed. The save posts a charge; the balance derives from the ledger until it is paid.</summary>
    Credit,
}

/// <summary>
/// An invoice — the new-side aggregate, mapped onto the adopted legacy <c>invoice_h</c> table.
/// </summary>
/// <remarks>
/// The legacy app is still live and still reads <c>invoice_h</c> (its payments and job-card modules,
/// and the new app's own Phase 4 reports, all read it — confirmed 2026-07-15). So this is <b>additive
/// adoption</b>, exactly as the master-data tables were: the migration adds a real primary key (the
/// table has none — Finding 6), the audit columns, and <b>new <c>decimal</c> columns</b> for the money,
/// while the legacy <c>varchar</c> money columns are left in place and <b>written alongside</b> on save,
/// so a legacy reader still sees a complete row. Those legacy-shadow columns are a persistence concern
/// (the save pipeline keeps them in step) — they are deliberately not properties here, so the domain
/// model is the honest typed one and nothing computes off a string.
///
/// <para>Two things this model does <i>not</i> carry, on purpose:</para>
/// <list type="bullet">
/// <item><b>No balance.</b> The legacy <c>balance</c> is a mutable string that produced Rs. 1.55M of
/// duplicate payments nobody could reconstruct (Finding 1, B3). Here the balance is <b>derived</b> from
/// the receivables ledger, never stored on the document. (The legacy <c>balance</c> column is still
/// written for legacy readers — but it is a shadow, not the truth.)</item>
/// <item><b>No per-line tax rate.</b> One rate per document — the company's — applied to every line
/// (the <c>one-vat-rate-per-document</c> decision). The resolved rate lives on the header, snapshotted,
/// so a reprint reproduces the figure it was issued with rather than re-resolving to today's rate (the
/// legacy <c>CURDATE()</c> bug, B5/B6).</item>
/// </list>
/// </remarks>
public class Invoice : IAuditable, ISoftDeletable
{
    /// <summary>Added by the migration; <c>invoice_h</c> has no key of any kind (Finding 6).</summary>
    public long Id { get; set; }

    /// <summary>The document number, allocated transactionally from <c>document_series</c> at save.</summary>
    public string Number { get; set; } = null!;

    /// <summary>
    /// The trading entity. Nullable only because it maps to the <c>company_id</c> the multi-company
    /// migration deliberately added as nullable ("a wrong company_id is worse than an absent one") —
    /// keeping it nullable is additive, where tightening it would not be. A new invoice always sets it.
    /// </summary>
    public long? CompanyId { get; set; }

    /// <summary>The customer, by surrogate key — a real reference, not the legacy <c>cuscode</c> string.</summary>
    public long CustomerId { get; set; }

    /// <summary>The document date — the date the tax rate is resolved as of. Typed, not the legacy varchar.</summary>
    public DateOnly Date { get; set; }

    public InvoiceType Type { get; set; }

    public string? PurchaseOrderNo { get; set; }
    public string? ContactPerson { get; set; }

    /// <summary>The user who raised it, by id — not the legacy <c>preparedby</c> name string.</summary>
    public long? PreparedBy { get; set; }

    // --- Money, in decimal, computed by the one tax engine and stored as resolved ---------------

    /// <summary>Σ of the line gross amounts, before the document discount (legacy <c>beforedisctot</c>).</summary>
    public decimal Subtotal { get; set; }

    /// <summary>The document-level discount percentage (legacy <c>discountper</c>).</summary>
    public decimal DiscountPercent { get; set; }

    public decimal DiscountAmount { get; set; }

    /// <summary>The taxable amount — subtotal less discount (legacy <c>novattotal</c>).</summary>
    public decimal NetTotal { get; set; }

    /// <summary>The rate the document was taxed at, or null when the company is not VAT-registered.</summary>
    public long? TaxRateId { get; set; }

    /// <summary>The rate percentage, snapshotted at save (legacy <c>vper</c>) — never re-resolved.</summary>
    public decimal TaxRatePercentage { get; set; }

    public decimal TaxAmount { get; set; }

    /// <summary>What the customer owes — the VAT-inclusive grand total (legacy <c>totamount</c>).</summary>
    public decimal Total { get; set; }

    /// <summary>Σ of the line costs — the cost-at-sale basis for profit (legacy <c>cost</c>).</summary>
    public decimal Cost { get; set; }

    /// <summary>
    /// The quotation this invoice was converted from, or null if it was raised directly. The back-link
    /// the legacy conversion never stored (plan §6) — paired with <c>Quotation.ConvertedToInvoiceId</c>,
    /// so the two documents point at each other.
    /// </summary>
    public long? SourceQuotationId { get; set; }

    /// <summary>
    /// <c>new</c> for documents this app raises; existing rows are <c>legacy</c>. Set once, never changed
    /// — it is the line the legacy payments screen is scoped by, so it can never touch a new invoice.
    /// </summary>
    public string DataOrigin { get; set; } = "new";

    public ICollection<InvoiceLine> Lines { get; set; } = new List<InvoiceLine>();

    public long? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public long? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public long? DeletedBy { get; set; }
    public DateTime? DeletedAt { get; set; }
    public int RowVersion { get; set; }
}
