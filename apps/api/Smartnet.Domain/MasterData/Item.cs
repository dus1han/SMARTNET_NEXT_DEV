using Smartnet.Domain.Auditing;

namespace Smartnet.Domain.MasterData;

/// <summary>
/// An item in the catalogue, mapped onto the legacy <c>item_m</c> table.
/// </summary>
/// <remarks>
/// <b>The legacy table has two columns: a code and a name.</b> No price, no cost, no tax rate, no
/// unit — nowhere for any of them to go. That is why every rate on every invoice for three years has
/// been typed by hand: there was never a default to type over.
/// <para>
/// And it shows. All 12,598 invoice lines carry an empty <c>itemcode</c>, though 780 of them have a
/// description that exactly matches an item's name — so item invoices <i>are</i> raised, and the save
/// copies the item's name into the line and discards which item it was. "How many of I-153 did we
/// sell?" has no answer today, because the join does not exist.
/// </para>
/// <para>
/// Slice 1 adopts the table as it stands: a key, an index, and the audit columns. <b>Slice 4 adds the
/// columns it never had</b> — selling price, cost, tax rate, reorder level, unit — and Phase 5's
/// document line carries a nullable <c>item_id</c>, which is the whole of the fix.
/// </para>
/// </remarks>
public class Item : IAuditable, ISoftDeletable
{
    /// <summary>Added by the migration. The legacy table has no primary key (Finding 6).</summary>
    public long Id { get; set; }

    /// <summary>"I-153". Unique, now that there is an index saying so.</summary>
    public string? Code { get; set; }

    public string? Name { get; set; }

    // --- The columns item_m never had (Slice 4) ----------------------------------------------
    // All nullable, all new: the legacy table has only code and name, so there is no value to
    // migrate and nothing here can break a legacy INSERT that names neither.

    /// <summary>The default price offered on a new item line — the "rate to type over" that never
    /// existed, which is why every line for three years was typed by hand. Money, never double.</summary>
    public decimal? SellingPrice { get; set; }

    /// <summary>What the item costs to buy. Feeds the margin the catalogue could never report.</summary>
    public decimal? Cost { get; set; }

    // No per-item tax rate: tax is decided on the document line (Phase 5), not carried on the item.
    // The business does not want it in the item master, and a shared item cannot sensibly carry one
    // company's rate anyway (tax_rates is per-company).

    /// <summary>The level at or below which the item shows on the "below reorder" list — the entire
    /// point of the field. Null means "not tracked".</summary>
    public decimal? ReorderLevel { get; set; }

    /// <summary>Free text — "pcs", "box", "m". The legacy schema has no unit at all.</summary>
    public string? Unit { get; set; }

    public long? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public long? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public long? DeletedBy { get; set; }
    public DateTime? DeletedAt { get; set; }
    public int RowVersion { get; set; }
}

/// <summary>
/// One receipt of stock for an item — the legacy <c>item_stock</c> table.
/// </summary>
/// <remarks>
/// Six rows, all entered on one day in September 2025, and no balance has moved since. That is not
/// neglect: nothing has ever been <i>linked</i> to an item, so nothing has ever consumed stock.
/// <para>
/// ⚠️ <b><see cref="Balance"/> is the legacy app's mutable balance column</b> — the same shape as
/// <c>invoice_h.balance</c>, which is ISSUES B3 and the thing that produced Rs. 1.55M of duplicate
/// payments nobody could reconstruct. It is adopted here, unchanged, because the legacy app still
/// reads it. <b>Nothing in the new application may write to it.</b> Slice 4 derives the balance from
/// an immutable movement ledger, and this column becomes a cache the ledger rebuilds — a decision
/// that is free to make now, with six rows, and expensive to make later.
/// </para>
/// </remarks>
public class StockBatch : IAuditable, ISoftDeletable
{
    /// <summary>Already AUTO_INCREMENT, but under a non-unique index. The migration makes it the key.</summary>
    public long Id { get; set; }

    public string? ItemCode { get; set; }

    /// <summary>Retyped from <c>varchar(100)</c> by the migration (Finding 5).</summary>
    public decimal? UnitCost { get; set; }

    /// <summary>Retyped from <c>varchar(100)</c>. Every value parses, so the retype is safe.</summary>
    public DateOnly? InDate { get; set; }

    public string? Warranty { get; set; }

    public decimal? Quantity { get; set; }

    /// <inheritdoc cref="StockBatch"/>
    public decimal? Balance { get; set; }

    /// <summary>
    /// The legacy attribution: a display <i>name</i> ("Chanaka Kotugoda"), not a user id. The same
    /// <c>addedby</c> pattern the audit spine exists to replace — rename the user and the history
    /// becomes ambiguous. Kept because the legacy app writes it; the new columns record the id.
    /// </summary>
    public string? EnteredBy { get; set; }

    public DateTime? EnteredAt { get; set; }

    public long? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public long? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public long? DeletedBy { get; set; }
    public DateTime? DeletedAt { get; set; }
    public int RowVersion { get; set; }
}

/// <summary>
/// A margin band — the legacy <c>profit_percent</c> table: 5%, 10%, 15%, 20%, 25%, 30%.
/// </summary>
/// <remarks>
/// Reference data, and one of the three tables in the entire legacy schema that has a primary key.
/// Not auditable: nobody edits it, and if that changes it can be made so in a line.
/// </remarks>
public class ProfitPercent
{
    public long Id { get; set; }

    /// <summary>The percentage, as the legacy table stores it — "5", "10".</summary>
    public string? Name { get; set; }
}
