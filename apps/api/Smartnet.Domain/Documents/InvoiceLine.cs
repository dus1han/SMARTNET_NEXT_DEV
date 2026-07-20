using Smartnet.Domain.Auditing;

namespace Smartnet.Domain.Documents;

/// <summary>
/// One line of an invoice, mapped onto the adopted legacy <c>invoice_l</c> table.
/// </summary>
/// <remarks>
/// A line is either an <b>item</b> line — it references an <see cref="ItemId"/>, carries a
/// <see cref="Cost"/>, and issues stock — or a <b>service</b> line, free-typed, with neither. That one
/// distinction is the whole of what the legacy app split into separate "item" and "service" controllers
/// (slice 0-B); here it is a nullable column. The legacy bug where an item invoice <i>threw away which
/// item it was</i> is fixed by <see cref="ItemId"/> being a real reference, with <see cref="ItemCode"/>
/// snapshotted beside it so the line still reads even if the item is later recoded.
///
/// <para>Money is <c>decimal</c>. The legacy <c>invoice_l</c> gains a primary key (it has none) and a
/// real foreign key to the invoice's new surrogate id; its legacy <c>varchar</c> columns are written
/// alongside for legacy readers, as on the header.</para>
///
/// <para>Tax is not on the line — one rate applies to the whole document (see <see cref="Invoice"/>).
/// The line carries what is line-specific: quantity, price, its own discount, and the resulting net.</para>
/// </remarks>
public class InvoiceLine : IAuditable
{
    /// <summary>Added by the migration; <c>invoice_l</c> has no key of its own.</summary>
    public long Id { get; set; }

    /// <summary>
    /// The parent invoice's surrogate id. Nullable because the legacy table it is adopted onto holds
    /// 608 orphan lines whose header no longer exists (Finding 3) — LEGACY-DATA-POLICY §3 keeps those in
    /// place under a nullable foreign key rather than deleting them. A line this app writes always has a
    /// parent; a legacy orphan has null.
    /// </summary>
    public long? InvoiceId { get; set; }

    /// <summary>The item, by surrogate key, or null for a free-typed service line.</summary>
    public long? ItemId { get; set; }

    /// <summary>The item's code as it stood at save — legible even if the item is later recoded.</summary>
    public string? ItemCode { get; set; }

    public string? Description { get; set; }

    public decimal Quantity { get; set; }

    /// <summary>The unit price (legacy <c>rate</c>).</summary>
    public decimal UnitPrice { get; set; }

    /// <summary>This line's own discount percentage — distinct from the document discount.</summary>
    public decimal DiscountPercent { get; set; }

    /// <summary>Quantity × price, before this line's discount (legacy <c>tot</c>).</summary>
    public decimal Gross { get; set; }

    /// <summary>The line's taxable amount — gross less its discount.</summary>
    public decimal Net { get; set; }

    /// <summary>The <b>unit</b> cost for this line — item lines only; null for a service line.</summary>
    /// <remarks>
    /// Per unit, not per line: it is copied from the item master, which prices one of a thing. The document's
    /// cost basis multiplies it by the quantity — see <see cref="DocumentCostBasis"/>, the one place that
    /// arithmetic lives, and which the quantity was missing from until 2026-07-20.
    /// </remarks>
    public decimal? Cost { get; set; }

    public Invoice Invoice { get; set; } = null!;

    public long? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public long? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public long? DeletedBy { get; set; }
    public DateTime? DeletedAt { get; set; }
    public int RowVersion { get; set; }
}
