namespace Smartnet.Domain.Documents;

/// <summary>
/// The document-level cost basis — <b>Σ (unit cost × quantity)</b> over the lines.
/// </summary>
/// <remarks>
/// <para>
/// A line's <c>Cost</c> is a <b>unit</b> cost. It is populated from the item master, which prices one of a
/// thing, and it stays a unit cost so that the edit screen can seed itself from the stored line without
/// having to divide a total back down by a quantity that may since have changed.
/// </para>
/// <para>
/// <b>The quantity used to be missing here</b>, and that is the defect this type exists to remove: every
/// creator and editor summed the bare line costs, so ten pumps costing 500 each recorded a cost basis of
/// 500 rather than 5,000. Nothing surfaced it — cost is not posted to the ledger and is never reconciled
/// against anything — it simply made <c>Profit = Total - Cost</c> report a margin that was too generous,
/// by a factor of the quantity, everywhere margin is shown.
/// </para>
/// <para>
/// One implementation, called from every document that carries a cost (invoice, quotation, credit note,
/// purchase order, and both the create and the edit path of each), because seven copies of an arithmetic
/// rule is how the seven of them came to disagree in the first place.
/// </para>
/// </remarks>
public static class DocumentCostBasis
{
    /// <summary>Σ (unit cost × quantity). A line with no cost contributes nothing.</summary>
    public static decimal Of(IEnumerable<(decimal? UnitCost, decimal Quantity)> lines) =>
        lines.Sum(line => (line.UnitCost ?? 0m) * line.Quantity);
}
