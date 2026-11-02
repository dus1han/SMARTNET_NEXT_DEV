using Smartnet.Domain.Auditing;

namespace Smartnet.Domain.Documents;

/// <summary>
/// A proforma invoice — the priced document a customer pays against before there is an invoice. Raised from
/// a quotation, or entered on its own; converting it raises the real invoice.
/// </summary>
/// <remarks>
/// <para><b>It charges nothing.</b> Like a quotation: no receivables-ledger entry, no stock movement, no
/// general-ledger posting, and it is not a tax invoice. All of that happens once, when it is converted, and
/// through the same save pipeline every invoice uses.</para>
///
/// <para><b>A table of its own, unlike the quotation.</b> The legacy app has no proforma at all, so there is
/// no legacy table to adopt and no shadow to keep in step — it lives only in the new app.</para>
///
/// <para><b>Not editable.</b> To change one, void it and raise a new one. For a proforma raised from a
/// quotation, voiding frees the quotation to be edited first — so the quotation, the proforma and the invoice
/// can never disagree about what was offered.</para>
///
/// <para><b>Numbered per company</b> from its own series (<see cref="Settings.DocumentTypes.Proforma"/>),
/// so each company's proformas run in their own sequence with their own prefix.</para>
/// </remarks>
public class ProformaInvoice : IAuditable, ISoftDeletable
{
    public long Id { get; set; }

    public string Number { get; set; } = null!;

    public long CompanyId { get; set; }

    /// <summary>
    /// The quotation this was raised from — new or legacy, both live in <c>quotation_h</c> — or null for a
    /// proforma entered on its own.
    /// </summary>
    public long? QuotationId { get; set; }

    /// <summary>The quotation's number as it stood, so the link reads without a join. Null with no quotation.</summary>
    public string? QuotationNumber { get; set; }

    public long CustomerId { get; set; }

    public DateOnly Date { get; set; }

    public string? ContactPerson { get; set; }

    /// <summary>How long the price holds — carried from the quotation.</summary>
    public string? Validity { get; set; }

    public long? PreparedBy { get; set; }

    public decimal Subtotal { get; set; }

    public decimal DiscountPercent { get; set; }

    public decimal DiscountAmount { get; set; }

    public decimal NetTotal { get; set; }

    /// <summary>The VAT rate in force on the proforma's date, snapshotted so a reprint matches.</summary>
    public long? TaxRateId { get; set; }

    public decimal TaxRatePercentage { get; set; }

    public decimal TaxAmount { get; set; }

    public decimal Total { get; set; }

    /// <summary>The invoice it became. Null until converted; converting is once only.</summary>
    public long? ConvertedToInvoiceId { get; set; }

    public DateTime? ConvertedAt { get; set; }

    public long? ConvertedBy { get; set; }

    public bool IsConverted => ConvertedToInvoiceId is not null;

    public ICollection<ProformaInvoiceLine> Lines { get; set; } = new List<ProformaInvoiceLine>();

    public long? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public long? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }

    /// <summary>Who voided it, and when. A voided proforma frees its quotation.</summary>
    public long? DeletedBy { get; set; }

    public DateTime? DeletedAt { get; set; }
    public int RowVersion { get; set; }
}

/// <summary>One line of a proforma — the quotation's line, valued at the proforma's date.</summary>
public class ProformaInvoiceLine : IAuditable
{
    public long Id { get; set; }

    public long ProformaInvoiceId { get; set; }

    /// <summary>The item, or null for a typed service line. Carried so the invoice issues stock.</summary>
    public long? ItemId { get; set; }

    public string? ItemCode { get; set; }

    public string? Description { get; set; }

    public decimal Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal DiscountPercent { get; set; }

    /// <summary>Quantity × price, before this line's discount.</summary>
    public decimal Gross { get; set; }

    /// <summary>Gross less this line's discount.</summary>
    public decimal Net { get; set; }

    /// <summary>The unit cost — item lines only — carried to the invoice as its cost basis.</summary>
    public decimal? Cost { get; set; }

    public ProformaInvoice ProformaInvoice { get; set; } = null!;

    public long? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public long? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public long? DeletedBy { get; set; }
    public DateTime? DeletedAt { get; set; }
    public int RowVersion { get; set; }
}
