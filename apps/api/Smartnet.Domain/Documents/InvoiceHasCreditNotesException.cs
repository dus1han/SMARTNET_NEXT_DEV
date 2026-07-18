namespace Smartnet.Domain.Documents;

/// <summary>
/// Thrown when an invoice with a live credit note against it is edited or voided.
/// </summary>
/// <remarks>
/// The mirror of <see cref="InvoiceHasPaymentsException"/>, and refused for the same reason: a credit note
/// is raised against the invoice's figures and states the amount it reverses. Changing or removing the
/// invoice underneath would leave the two disagreeing, with nothing on either document to say which is
/// right — and would leave the credit stranded against an invoice that no longer says what it credited.
///
/// <para>The linking document is removed first: void the credit note, then edit or void the invoice.</para>
/// </remarks>
public sealed class InvoiceHasCreditNotesException(string number)
    : Exception($"Invoice {number} has a credit note against it. Void the credit note first, then edit or void the invoice.")
{
    public string Number { get; } = number;
}
