namespace Smartnet.Domain.Documents;

/// <summary>
/// A quotation that has already been converted into an invoice cannot be converted again.
/// </summary>
/// <remarks>
/// This is the guard the legacy app never had: its conversion was re-runnable, so one quote could issue
/// several invoices and decrement stock several times (plan §6). Here a converted quote carries the id
/// of the invoice it became, and a second attempt is refused — with that id, so the caller can go to the
/// invoice that already exists rather than making another.
/// </remarks>
public sealed class QuotationAlreadyConvertedException(string quotationNumber, long invoiceId)
    : Exception($"Quotation {quotationNumber} has already been converted to an invoice (id {invoiceId}).")
{
    public string QuotationNumber { get; } = quotationNumber;

    public long InvoiceId { get; } = invoiceId;
}
