namespace Smartnet.Domain.Documents;

/// <summary>
/// The terms under which a quotation becomes an invoice — the few things the quote does not itself
/// decide.
/// </summary>
/// <remarks>
/// The lines, the customer, the company and the document discount all come from the quotation. What the
/// caller supplies is what makes it a <i>sale</i>: whether it is <see cref="Type"/> cash or credit, the
/// invoice <see cref="Date"/> (the invoice is taxed at its own date, not the quote's), and optionally a
/// PO number and contact for the invoice.
/// </remarks>
public sealed record ConvertQuotation(
    InvoiceType Type,
    DateOnly Date,
    string? PurchaseOrderNo,
    string? ContactPerson);

/// <summary>
/// Converts a quotation into an invoice — correctly, unlike the legacy copy-paste (Phase 5, slice 3).
/// </summary>
/// <remarks>
/// The legacy conversion copied a quote's lines into a new invoice, but <b>never marked the quote
/// converted</b> and stored <b>no back-link</b>, so the same quote could be converted again and again,
/// issuing stock each time (plan §6). This does three things it did not:
/// <list type="number">
/// <item>Builds the invoice through the <b>same save pipeline</b> as a hand-keyed one — a real number, a
/// ledger charge, a stock issue and a snapshot — so a converted invoice is indistinguishable from any
/// other.</item>
/// <item><b>Marks the quotation converted</b>, with a back-link to the invoice (and the invoice a
/// back-link to the quotation), all in one transaction.</item>
/// <item><b>Refuses a second conversion</b> of an already-converted quote.</item>
/// </list>
/// The credit limit gates the resulting invoice exactly as it gates a hand-keyed one — conversion is a
/// sale.
/// </remarks>
public interface IQuotationConverter
{
    Task<InvoiceCreated> ConvertAsync(
        long quotationId,
        ConvertQuotation request,
        CancellationToken cancellationToken = default);
}
