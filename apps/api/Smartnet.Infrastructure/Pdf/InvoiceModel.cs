namespace Smartnet.Infrastructure.Pdf;

/// <summary>One priced line of an invoice — the five detail columns the legacy report carried.</summary>
public sealed record InvoiceItem(
    string ItemNo,
    string Description,
    decimal Quantity,
    decimal Rate,
    decimal Total);

/// <summary>
/// What an invoice renders from.
/// </summary>
/// <remarks>
/// The <c>Invoice_ST</c> replacement — the non-VAT invoice, and only that. Its twelve legacy parameters
/// are all here: date, qno, client, address, contactP, tot, preparedby, pono, paid, balance, idisc,
/// totafterdisc (REPORT-FIELDS.md).
///
/// <para>The VAT pair is not modelled. <c>Invoice_SN_TAX</c> carries a supplier/purchaser registration
/// block that this record has no fields for, and adding a nullable tax amount here would let a
/// VAT-registered company print an invoice that silently charges no VAT. That is a separate document,
/// built separately.</para>
///
/// <para>Money arrives as <c>decimal</c> and is formatted at render time. The legacy report received
/// pre-formatted strings, which froze every rounding decision upstream and out of sight.</para>
/// </remarks>
public sealed record InvoiceModel(
    byte[]? Logo,
    string CompanyName,
    string CompanyContact,
    string? AccentColour,
    string InvoiceNo,
    string Date,

    /// <summary>"Cash" or "Credit" — printed as a reference so the terms are on the document.</summary>
    string InvoiceType,

    /// <summary>The customer's own order number. Empty when they gave none.</summary>
    string PoNumber,

    string ClientName,
    string ClientAddress,

    /// <summary>Already combined as "Name (telephone)" by the renderer, as on every other document.</summary>
    string ContactPerson,

    string PreparedBy,
    IReadOnlyList<InvoiceItem> Items,
    decimal Subtotal,
    decimal DiscountPercent,
    decimal DiscountAmount,
    decimal NetTotal,

    decimal Total,

    /// <summary>What has been received against this invoice. Zero on an unpaid one.</summary>
    decimal Paid,

    /// <summary>What is still owed — the legacy <c>balance</c> column, not a recomputation of it.</summary>
    decimal BalanceDue,

    /// <summary>The payment block. Null when the company has no bank details on file.</summary>
    BankDetails? Bank);
