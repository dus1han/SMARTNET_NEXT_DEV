using Smartnet.Domain.Auditing;

namespace Smartnet.Domain.Documents;

/// <summary>
/// A quotation — the new-side aggregate, mapped onto the adopted legacy <c>quotation_h</c> table.
/// </summary>
/// <remarks>
/// The same document as an <see cref="Invoice"/>, minus the two things that make an invoice a sale: a
/// quotation <b>charges nothing and issues nothing</b> (Phase 5, slice 3). There is no ledger entry and
/// no stock movement behind it — it is a priced offer, not a receivable. So this model carries no
/// settlement <c>Type</c> (an invoice's cash/credit), no balance and no ledger; what it adds is a
/// <see cref="Validity"/> (how long the price holds) and the <b>conversion link</b>.
///
/// <para><b>Additive adoption</b>, exactly as invoices were: the migration adds a primary key
/// (<c>quotation_h</c> has none), the audit columns, and new <c>decimal</c>/<c>date</c> columns beside
/// the legacy <c>varchar</c> ones, which the save writes alongside so the surviving legacy reader (a
/// customer's quote history) still sees a complete row. The tax rate is resolved at the quotation's date
/// and snapshotted onto the header (the <c>one-vat-rate-per-document</c> decision), so a reprint
/// reproduces the figures it was issued with.</para>
///
/// <para><b>Conversion is one-way and once-only.</b> <see cref="ConvertedToInvoiceId"/> is the back-link
/// the legacy copy-paste conversion never had: once set, the quote is spent and a second conversion is
/// refused — closing the bug where the same quote could be converted repeatedly, issuing stock each time
/// (plan §6). The conversion builds the invoice through the <i>same</i> save pipeline, so the invoice
/// gets a real number, a ledger charge, a stock issue and a snapshot — none of which the legacy
/// conversion produced.</para>
/// </remarks>
public class Quotation : IAuditable, ISoftDeletable
{
    /// <summary>Added by the migration; <c>quotation_h</c> has no key of any kind.</summary>
    public long Id { get; set; }

    /// <summary>The quotation number, allocated transactionally from <c>document_series</c> at save.</summary>
    public string Number { get; set; } = null!;

    /// <summary>
    /// The trading entity. Nullable only because it maps to the <c>company_id</c> the multi-company
    /// migration added as nullable; a new quotation always sets it.
    /// </summary>
    public long? CompanyId { get; set; }

    /// <summary>The customer, by surrogate key — a real reference, not the legacy <c>customer</c> code.</summary>
    public long CustomerId { get; set; }

    /// <summary>The document date — the date the tax rate is resolved as of. Typed, not the legacy varchar.</summary>
    public DateOnly Date { get; set; }

    public string? ContactPerson { get; set; }

    /// <summary>
    /// How long the quoted price holds (the legacy <c>q_valid</c>) — free text such as "30 Days". A
    /// quotation-only field; an invoice has no equivalent.
    /// </summary>
    public string? Validity { get; set; }

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

    /// <summary>The rate the document was quoted at, or null when the company is not VAT-registered.</summary>
    public long? TaxRateId { get; set; }

    /// <summary>The rate percentage, snapshotted at save (legacy <c>vper</c>) — never re-resolved.</summary>
    public decimal TaxRatePercentage { get; set; }

    public decimal TaxAmount { get; set; }

    /// <summary>The VAT-inclusive grand total quoted (legacy <c>totamount</c>).</summary>
    public decimal Total { get; set; }

    /// <summary>Σ of the line costs — the cost basis for the quoted margin (legacy <c>quotecost</c>).</summary>
    public decimal Cost { get; set; }

    // --- Conversion --------------------------------------------------------------------------------

    /// <summary>
    /// The invoice this quotation was converted into, or null while it is still an open offer. Set once,
    /// at conversion; a quotation with this set is spent and cannot be converted again.
    /// </summary>
    public long? ConvertedToInvoiceId { get; set; }

    /// <summary>When the conversion happened. Null until converted.</summary>
    public DateTime? ConvertedAt { get; set; }

    /// <summary>Who converted it, by user id. Null until converted.</summary>
    public long? ConvertedBy { get; set; }

    /// <summary>True once the quotation has been turned into an invoice.</summary>
    public bool IsConverted => ConvertedToInvoiceId is not null;

    /// <summary>
    /// <c>new</c> for quotations this app raises; existing legacy rows are <c>legacy</c> and are never
    /// read as a <see cref="Quotation"/> (a query filter excludes them).
    /// </summary>
    public string DataOrigin { get; set; } = "new";

    public ICollection<QuotationLine> Lines { get; set; } = new List<QuotationLine>();

    public long? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public long? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public long? DeletedBy { get; set; }
    public DateTime? DeletedAt { get; set; }
    public int RowVersion { get; set; }
}
