using Smartnet.Domain.Auditing;

namespace Smartnet.Domain.Documents;

/// <summary>
/// One line of a credit note, mapped onto the adopted legacy <c>cn_l</c> table.
/// </summary>
/// <remarks>
/// The same shape as an <see cref="InvoiceLine"/> (slice 0-B): a line is either an <b>item</b> line — it
/// references an <see cref="ItemId"/>, carries a <see cref="Cost"/>, and (when the note returns goods)
/// receives stock — or a free-typed <b>service</b> line, with neither. Money is <c>decimal</c>; the legacy
/// <c>cn_l</c> gains a primary key (it has none) and a real foreign key to the note's surrogate id, its
/// legacy <c>varchar</c> columns written alongside for legacy readers. Tax is on the document, not the line.
/// </remarks>
public class CreditNoteLine : IAuditable
{
    /// <summary>Added by the migration; <c>cn_l</c> has no key of its own.</summary>
    public long Id { get; set; }

    /// <summary>The parent credit note's surrogate id. Nullable to match the adopted table's legacy rows.</summary>
    public long? CreditNoteId { get; set; }

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

    /// <summary>The cost basis for this line — item lines only; null for a service line.</summary>
    public decimal? Cost { get; set; }

    public CreditNote CreditNote { get; set; } = null!;

    public long? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public long? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public long? DeletedBy { get; set; }
    public DateTime? DeletedAt { get; set; }
    public int RowVersion { get; set; }
}
