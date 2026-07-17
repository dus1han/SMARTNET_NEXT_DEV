using Smartnet.Domain.Auditing;

namespace Smartnet.Domain.MasterData;

/// <summary>
/// An expense category (Phase 7, slice 3) — a mini-master on the adopted legacy <c>exp_cat_m</c> table.
/// </summary>
/// <remarks>
/// Shared across companies (the legacy app never scoped them). Adopted additively: its AUTO_INCREMENT id under
/// a non-unique key is promoted to a real primary key (Finding 6), and audit columns are added. New categories
/// append; a rename is audited; delete is soft.
/// </remarks>
public class ExpenseCategory : IAuditable, ISoftDeletable
{
    public long Id { get; set; }

    /// <summary>The category name (the legacy <c>expcatname</c>).</summary>
    public string? Name { get; set; }

    public long? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public long? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public long? DeletedBy { get; set; }
    public DateTime? DeletedAt { get; set; }
    public int RowVersion { get; set; }
}
