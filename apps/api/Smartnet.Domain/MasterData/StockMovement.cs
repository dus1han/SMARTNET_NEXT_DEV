using Smartnet.Domain.Auditing;

namespace Smartnet.Domain.MasterData;

/// <summary>
/// Why a stock quantity changed. Only <see cref="Adjustment"/> is written in Phase 3; the document
/// engine adds the rest in Phase 5.
/// </summary>
public enum StockMovementType
{
    /// <summary>A manual correction — a count, a write-off, an opening balance. The only kind a human
    /// enters directly.</summary>
    Adjustment,

    /// <summary>Stock received against a purchase order (Phase 6/5).</summary>
    Receipt,

    /// <summary>Stock consumed by an invoice (Phase 5).</summary>
    Issue,
}

/// <summary>
/// One entry in the immutable stock ledger — the single reason a quantity ever moves.
/// </summary>
/// <remarks>
/// This is ISSUES B3 answered before there is any stock to get wrong. The legacy app keeps a mutable
/// <c>item_stock.balance</c> and edits it in place; that is the same shape as <c>invoice_h.balance</c>,
/// the column that produced Rs. 1.55M of duplicate payments nobody could reconstruct. Here a balance
/// is never stored and never edited — it is <b>derived</b>, always, as the sum of these movements
/// (<c>Item</c> balance = Σ <see cref="Quantity"/>). A mistake is corrected by a second, compensating
/// movement, never by rewriting a number, so the whole history of a quantity is always readable.
/// <para>
/// Append-only by design: this is <see cref="IAuditable"/> (it records who entered it and when) but
/// deliberately <b>not</b> <see cref="ISoftDeletable"/>. There is no update path and no delete path in
/// the application — a ledger you can edit is not a ledger.
/// </para>
/// </remarks>
public class StockMovement : IAuditable
{
    public long Id { get; set; }

    /// <summary>The item this moved, by surrogate key — a real reference, unlike the legacy
    /// code-string links that made the item join impossible to trust.</summary>
    public long ItemId { get; set; }

    public StockMovementType Type { get; set; }

    /// <summary>
    /// Signed. A positive quantity increases stock (a receipt, an upward correction); a negative one
    /// decreases it (an issue, a write-off). The item's balance is the sum of these, so the sign is
    /// the whole of the arithmetic.
    /// </summary>
    public decimal Quantity { get; set; }

    /// <summary>Why the adjustment was made — a stock count, breakage, an opening balance. Required on
    /// an adjustment (it is a change with a mandatory reason, like every other).</summary>
    public string? Reason { get; set; }

    /// <summary>When the movement happened, as distinct from when the row was written
    /// (<see cref="CreatedAt"/>) — an opening balance may be back-dated to the count.</summary>
    public DateTime OccurredAt { get; set; }

    public long? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }

    // Present because IAuditable carries them and the audit interceptor stamps uniformly; never
    // written, because a ledger entry is never updated or deleted.
    public long? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public long? DeletedBy { get; set; }
    public DateTime? DeletedAt { get; set; }
    public int RowVersion { get; set; }
}
