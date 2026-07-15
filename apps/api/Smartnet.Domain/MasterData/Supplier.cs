using Smartnet.Domain.Auditing;

namespace Smartnet.Domain.MasterData;

/// <summary>
/// A supplier, mapped onto the legacy <c>sup_m</c> table.
/// </summary>
/// <remarks>
/// The simplest of the master tables — and the one the legacy app cannot delete from at all. There is
/// no delete action anywhere in <c>SupplierController</c>: a supplier you stop buying from stays in
/// the picker forever. Here a delete is a soft delete, so it disappears from the list and its history
/// stays attributable.
/// </remarks>
public class Supplier : IAuditable, ISoftDeletable
{
    /// <summary>Added by the migration. The legacy table has no primary key (Finding 6).</summary>
    public long Id { get; set; }

    public string? Code { get; set; }
    public string? Name { get; set; }
    public string? ContactPerson { get; set; }
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? VatNumber { get; set; }

    public long? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public long? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public long? DeletedBy { get; set; }
    public DateTime? DeletedAt { get; set; }
    public int RowVersion { get; set; }
}
