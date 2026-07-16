using Smartnet.Domain.Auditing;

namespace Smartnet.Domain.Documents;

/// <summary>
/// One line of a job card — a single serial-tracked unit of the customer's equipment (Phase 6, slice 3).
/// </summary>
/// <remarks>
/// The structured replacement for the legacy <c>jobs_m.items</c> text blob: one row per unit, each carrying
/// its own <see cref="Serial"/>, so serials are real data rather than a fragile <c>" | "</c>-delimited
/// string. This is a genuinely new table (<c>jobcard_l</c>) — the legacy job card had no line table at all.
/// A line optionally references an <see cref="ItemId"/> (the customer's equipment may be an item we sell),
/// but carries no cost, price or stock — a job card moves no inventory.
/// </remarks>
public class JobCardLine : IAuditable
{
    public long Id { get; set; }

    /// <summary>The parent job card's surrogate id.</summary>
    public long? JobCardId { get; set; }

    /// <summary>An optional reference to the item master, where the equipment is something we sell. Usually null.</summary>
    public long? ItemId { get; set; }

    /// <summary>What the unit is — the equipment description.</summary>
    public string? Description { get; set; }

    /// <summary>The unit's serial number — one per row, the per-unit tracking the legacy blob could not do.</summary>
    public string? Serial { get; set; }

    /// <summary>Display order within the card.</summary>
    public int Sort { get; set; }

    public JobCard JobCard { get; set; } = null!;

    public long? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public long? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public long? DeletedBy { get; set; }
    public DateTime? DeletedAt { get; set; }
    public int RowVersion { get; set; }
}
