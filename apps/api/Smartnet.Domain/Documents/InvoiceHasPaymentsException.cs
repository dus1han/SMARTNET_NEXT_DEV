namespace Smartnet.Domain.Documents;

/// <summary>
/// Thrown when an edit is attempted on an invoice that has a payment against it (in full or in part) — a
/// cash invoice's settlement counts. A paid invoice is not edited: the payment must be deleted first, so the
/// figures the money was taken against are never changed underneath a payment that has already been made.
/// </summary>
public sealed class InvoiceHasPaymentsException(string number)
    : Exception($"Invoice {number} has a payment against it and cannot be edited. Delete the payment first, then edit the invoice.")
{
    public string Number { get; } = number;
}
