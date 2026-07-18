namespace Smartnet.Infrastructure.Pdf;

/// <summary>One credited line.</summary>
public sealed record CreditNoteItem(
    string Description,
    decimal Quantity,
    decimal Rate,
    decimal Total);

/// <summary>
/// What a credit note renders from.
/// </summary>
/// <remarks>
/// A credit note is an invoice run backwards, so it prints like one — with the invoice it credits named
/// on its face, because a credit that does not say what it reverses is unreconcilable. Whether stock came
/// back with it is stated too: the same document covers a return of goods and a pure price adjustment,
/// and the difference matters to whoever receives it.
/// </remarks>
public sealed record CreditNoteModel(
    byte[]? Logo,
    string CompanyName,
    string CompanyContact,
    string? AccentColour,
    string CreditNoteNo,
    string Date,

    /// <summary>The invoice this credits — the document's whole reason for existing.</summary>
    string InvoiceNo,

    string ClientName,
    string ClientAddress,

    /// <summary>The person the parent invoice was addressed to — a credit note carries none of its own.</summary>
    string ContactPerson,

    /// <summary>The customer's telephone, already grouped for reading.</summary>
    string ContactPhone,

    string PreparedBy,

    /// <summary>Whether the goods came back, or the credit is an adjustment only.</summary>
    bool ReturnsStock,

    IReadOnlyList<CreditNoteItem> Items,
    decimal Subtotal,
    decimal DiscountPercent,
    decimal DiscountAmount,
    decimal NetTotal,

    /// <summary>The VAT label, e.g. "VAT (18%)" — null when the company is not VAT-registered.</summary>
    string? TaxLabel,

    /// <summary>Null when the company is not VAT-registered, which omits the VAT rows entirely.</summary>
    decimal? TaxAmount,

    decimal Total);
