using Smartnet.Domain.Auditing;

namespace Smartnet.Domain.Documents;

/// <summary>
/// One line of a quotation, mapped onto the adopted legacy <c>quotation_l</c> table.
/// </summary>
/// <remarks>
/// The mirror of <see cref="InvoiceLine"/>: a line is either an <b>item</b> line — it references an
/// <see cref="ItemId"/> and carries a <see cref="Cost"/> — or a free-typed <b>service</b> line, with
/// neither. Unlike an invoice line, an item line here <i>does not</i> issue stock: a quotation is an
/// offer, so nothing leaves the shelf until it is converted to an invoice. The cost is still carried,
/// as the basis for the quoted margin. One rate applies to the whole document, so tax is not on the line.
///
/// <para>Money is <c>decimal</c>. <c>quotation_l</c> gains a primary key (it has none) and a real foreign
/// key to the quotation's new surrogate id; its legacy <c>varchar</c> columns are written alongside for
/// the surviving legacy reader.</para>
/// </remarks>
public class QuotationLine : IAuditable
{
    /// <summary>Added by the migration; <c>quotation_l</c> has no key of its own.</summary>
    public long Id { get; set; }

    /// <summary>
    /// The parent quotation's surrogate id. Nullable to permit any legacy orphan lines to keep a null
    /// parent rather than being deleted (LEGACY-DATA-POLICY §3); a line this app writes always has one.
    /// </summary>
    public long? QuotationId { get; set; }

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

    /// <summary>Quantity × price, before this line's discount (legacy <c>total</c>).</summary>
    public decimal Gross { get; set; }

    /// <summary>The line's taxable amount — gross less its discount.</summary>
    public decimal Net { get; set; }

    /// <summary>The cost basis for this line — item lines only; null for a service line.</summary>
    public decimal? Cost { get; set; }

    public Quotation Quotation { get; set; } = null!;

    public long? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public long? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public long? DeletedBy { get; set; }
    public DateTime? DeletedAt { get; set; }
    public int RowVersion { get; set; }
}
