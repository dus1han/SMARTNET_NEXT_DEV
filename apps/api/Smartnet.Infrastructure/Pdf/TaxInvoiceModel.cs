namespace Smartnet.Infrastructure.Pdf;

/// <summary>
/// One party to a taxable supply — who they are, where they are, how to reach them, and the
/// registration number the VAT rules require.
/// </summary>
/// <remarks>
/// The supplier and the purchaser carry exactly the same four fields on a tax invoice, which is why
/// they share a record: the legacy report declared each of the eight separately and printed them into
/// two hand-positioned columns, so the two sides could and did drift apart.
/// </remarks>
public sealed record TaxParty(string Tin, string Name, string Address, string Telephone);

/// <summary>
/// What a tax invoice renders from — the <c>Invoice_SN_TAX</c> replacement.
/// </summary>
/// <remarks>
/// A tax invoice is not an invoice with VAT added. It is the document a registered purchaser reclaims
/// against, so it has to name both parties' registration numbers, both addresses, both telephone
/// numbers and the date of supply. That is why this is a separate template rather than a flag on
/// <see cref="InvoiceModel"/>: the shape of the document is different, not just its arithmetic.
///
/// <para><b>Date of supply is its own field</b>, and is not assumed to be the invoice date. They are
/// the same on most invoices and were the same on every legacy one, but they are different questions —
/// the invoice date is when the document was raised, the date of supply is when the goods changed
/// hands, and only the second one decides which VAT period the supply belongs to.</para>
/// </remarks>
public sealed record TaxInvoiceModel(
    byte[]? Logo,
    string CompanyName,
    string CompanyContact,
    string? AccentColour,
    string InvoiceNo,
    string Date,
    string DateOfSupply,

    /// <summary>The company raising the invoice, with its own TIN.</summary>
    TaxParty Supplier,

    /// <summary>The customer being charged, with theirs.</summary>
    TaxParty Purchaser,

    string ContactPerson,
    string PoNumber,
    string PreparedBy,
    IReadOnlyList<InvoiceItem> Items,
    decimal Subtotal,
    decimal DiscountPercent,
    decimal DiscountAmount,
    decimal NetTotal,

    /// <summary>The VAT label, e.g. "VAT (18%)".</summary>
    string TaxLabel,

    decimal TaxAmount,
    decimal Total,

    /// <summary>What has been received against this invoice. Zero on an unpaid one.</summary>
    decimal Paid,

    /// <summary>What is still owed — the legacy <c>balance</c> column, not a recomputation of it.</summary>
    decimal BalanceDue,

    /// <summary>The payment block. Null when the company has no bank details on file.</summary>
    BankDetails? Bank);
