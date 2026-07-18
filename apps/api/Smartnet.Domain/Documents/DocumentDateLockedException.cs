namespace Smartnet.Domain.Documents;

/// <summary>
/// Thrown when a document's date is changed after something has come to depend on it.
/// </summary>
/// <remarks>
/// The date is not a cosmetic field. It is the date the VAT rate is resolved at, the date the ledger and
/// stock entries are recorded on, and the period the document falls into for a return. Moving it once
/// another document has been raised against this one — a credit note against an invoice, an invoice
/// converted from a quotation — would leave the pair straddling two periods and rated at two different
/// dates, with nothing on either document to say so.
///
/// <para>Everything else about the document stays editable; it is only the date that locks. The remedy is
/// to void the dependent document first, or to void this one and re-issue it under the date wanted.</para>
/// </remarks>
public sealed class DocumentDateLockedException(string number, string dependsOn)
    : Exception($"The date of {number} cannot be changed because {dependsOn}. Void that first, or void this document and re-issue it under the date you want.")
{
    public string Number { get; } = number;

    /// <summary>What depends on this document, said in the words the user will see.</summary>
    public string DependsOn { get; } = dependsOn;
}
