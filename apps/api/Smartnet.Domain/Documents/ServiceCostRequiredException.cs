namespace Smartnet.Domain.Documents;

/// <summary>
/// A service quotation cannot become an invoice until someone says what it cost.
/// </summary>
/// <remarks>
/// <para>
/// An <b>item</b> quotation knows its cost basis the moment it is raised: every line references an item,
/// and the item master carries that item's cost. A <b>service</b> quotation has no such source — what the
/// work costs is not settled until it is committed to — so the figure is asked for at conversion, which is
/// the point at which it becomes real.
/// </para>
/// <para>
/// Refusing the conversion rather than defaulting to zero is the whole point. Cost feeds
/// <c>Profit = Total - Cost</c> in the sales report and the dashboard, and a missing cost does not read as
/// missing there — it reads as a sale with <i>100% margin</i>. A wrong number that looks plausible is worse
/// than a blocked conversion, because nobody goes looking for it.
/// </para>
/// </remarks>
public sealed class ServiceCostRequiredException(string quotationNumber)
    : Exception(
        $"Quotation {quotationNumber} is a service quotation, so its cost must be entered to convert it. " +
        "Without one the invoice would report as pure profit.")
{
    public string QuotationNumber { get; } = quotationNumber;
}
