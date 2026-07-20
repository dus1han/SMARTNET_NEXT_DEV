using Smartnet.Domain.Auditing;

namespace Smartnet.Domain.Documents;

/// <summary>
/// One line of a purchase order, mapped onto the adopted legacy <c>po_l</c> table.
/// </summary>
/// <remarks>
/// The mirror of <see cref="QuotationLine"/>: a line is either an <b>item</b> line — it references an
/// <see cref="ItemId"/> and carries a <see cref="Cost"/> — or a free-typed <b>service</b> line, with
/// neither. An item line here <i>does not</i> receipt stock: a PO is an order, so nothing enters
/// inventory until the (deferred) GRN receives it; the <see cref="ItemId"/> is carried so that GRN can.
/// The legacy <c>po_l</c> line was pure free text (its <c>itemno</c> held a cart sequence number, not a
/// real item), so the item linkage — <see cref="ItemId"/> and <see cref="ItemCode"/> — is new; one rate
/// applies to the whole document, so tax is not on the line.
///
/// <para>Money is <c>decimal</c>. <c>po_l</c> gains a primary key (it has none) and a real foreign key to
/// the PO's new surrogate id; its legacy <c>varchar</c> columns (<c>pono</c>, <c>qty</c>, <c>rate</c>,
/// <c>total</c>) are written alongside for the surviving legacy reader. <c>po_l</c> has no legacy
/// <c>itemcode</c> or <c>cost</c> column, so both are new.</para>
/// </remarks>
public class PurchaseOrderLine : IAuditable
{
    /// <summary>Added by the migration; <c>po_l</c> has no key of its own.</summary>
    public long Id { get; set; }

    /// <summary>
    /// The parent PO's surrogate id. Nullable to permit any legacy orphan lines to keep a null parent
    /// rather than being deleted (LEGACY-DATA-POLICY §3); a line this app writes always has one.
    /// </summary>
    public long? PurchaseOrderId { get; set; }

    /// <summary>The item, by surrogate key, or null for a free-typed service line.</summary>
    public long? ItemId { get; set; }

    /// <summary>The item's code as it stood at save — a new column (<c>po_l</c> had no <c>itemcode</c>).</summary>
    public string? ItemCode { get; set; }

    public string? Description { get; set; }

    public decimal Quantity { get; set; }

    /// <summary>The unit price (legacy <c>rate</c>).</summary>
    public decimal UnitPrice { get; set; }

    /// <summary>This line's own discount percentage — distinct from the document discount.</summary>
    public decimal DiscountPercent { get; set; }

    /// <summary>Quantity × price, before this line's discount (legacy <c>total</c>).</summary>
    public decimal Gross { get; set; }

    /// <summary>The line's taxable amount — gross less its discount.</summary>
    public decimal Net { get; set; }

    /// <summary>
    /// The <b>unit</b> cost for this line — item lines only; null for a service line. A new column.
    /// </summary>
    /// <remarks>
    /// Per unit, not per line: it is copied from the item master, which prices one of a thing. The document's
    /// cost basis multiplies it by the quantity — see <see cref="DocumentCostBasis"/>, the one place that
    /// arithmetic lives, and which the quantity was missing from until 2026-07-20.
    /// </remarks>
    public decimal? Cost { get; set; }

    public PurchaseOrder PurchaseOrder { get; set; } = null!;

    public long? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public long? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public long? DeletedBy { get; set; }
    public DateTime? DeletedAt { get; set; }
    public int RowVersion { get; set; }
}
