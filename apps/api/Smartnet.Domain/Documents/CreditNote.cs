using Smartnet.Domain.Auditing;

namespace Smartnet.Domain.Documents;

/// <summary>
/// A credit note — the new-side aggregate, mapped onto the adopted legacy <c>cn_h</c> table. It is the
/// mirror of an <see cref="Invoice"/>: it is raised against a parent invoice and posts the <b>opposite</b>
/// ledger sign (a <see cref="Smartnet.Domain.Ledger.LedgerEntryType.Credit"/>, reducing the customer's
/// balance), and — where the note returns goods — a stock <b>receipt</b> back into stock.
/// </summary>
/// <remarks>
/// Additive adoption, exactly as <see cref="Invoice"/> and <see cref="Quotation"/> were: the migration adds
/// a real primary key (<c>cn_h</c> has none), the audit columns, and new <c>decimal</c> columns for the
/// money, while the legacy <c>varchar</c> columns are left in place and written alongside on save. Two
/// things it does <b>not</b> carry, on purpose, are the same two the invoice does not: no stored balance
/// (a credit is a ledger entry, never a mutated column — B3), and no per-line tax rate (one rate per
/// document).
///
/// <para>Unlike an invoice, a credit note carries no cash/credit <c>Type</c> and no credit-limit gate — it
/// only ever <i>reduces</i> what a customer owes. Its VAT rate is <b>inherited from the parent invoice's
/// snapshot</b> rather than re-resolved at the note's own date, so a full credit nets exactly against the
/// invoice it reverses even if the company's rate has since changed, and crediting an old legacy invoice
/// never depends on the rate table still covering that invoice's date.</para>
/// </remarks>
public class CreditNote : IAuditable, ISoftDeletable
{
    /// <summary>Added by the migration; <c>cn_h</c> has no key of any kind.</summary>
    public long Id { get; set; }

    /// <summary>The document number, allocated transactionally from <c>document_series</c> at save.</summary>
    public string Number { get; set; } = null!;

    /// <summary>
    /// The trading entity. Nullable to match the <c>company_id</c> the multi-company migration added to
    /// <c>cn_h</c> (backfilled from the parent invoice); a new credit note always sets it.
    /// </summary>
    public long? CompanyId { get; set; }

    /// <summary>The customer, by surrogate key — taken from the parent invoice, not entered separately.</summary>
    public long CustomerId { get; set; }

    /// <summary>
    /// The invoice this credit note is raised against, by surrogate id. The credit reduces this invoice's
    /// outstanding through the ledger (the entry carries this same id), so the note and the invoice net.
    /// </summary>
    public long InvoiceId { get; set; }

    /// <summary>The document date — the ledger entry's <c>OccurredAt</c> and the number's date.</summary>
    public DateOnly Date { get; set; }

    /// <summary>
    /// Whether this note returns the goods to stock — the legacy <c>stockposting</c> flag. When set, each
    /// item line posts a <see cref="Smartnet.Domain.MasterData.StockMovementType.Receipt"/> back into
    /// stock; a pure price adjustment leaves stock untouched.
    /// </summary>
    public bool ReturnsStock { get; set; }

    /// <summary>The user who raised it, by id — not the legacy <c>preparedby</c> name string.</summary>
    public long? PreparedBy { get; set; }

    // --- Money, in decimal, computed by the one tax engine and stored as resolved ---------------

    public decimal Subtotal { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal DiscountAmount { get; set; }

    /// <summary>The taxable amount — subtotal less discount (legacy <c>novattotal</c>).</summary>
    public decimal NetTotal { get; set; }

    /// <summary>The rate the note was taxed at — inherited from the parent invoice's snapshot.</summary>
    public long? TaxRateId { get; set; }

    /// <summary>The rate percentage, snapshotted at save (legacy <c>vper</c>) — never re-resolved.</summary>
    public decimal TaxRatePercentage { get; set; }

    public decimal TaxAmount { get; set; }

    /// <summary>The VAT-inclusive amount credited (legacy <c>totamount</c>).</summary>
    public decimal Total { get; set; }

    /// <summary>Σ of the line costs — the cost basis of the goods credited.</summary>
    public decimal Cost { get; set; }

    /// <summary><c>new</c> for notes this app raises; existing rows are <c>legacy</c>. Set once, never changed.</summary>
    public string DataOrigin { get; set; } = "new";

    public ICollection<CreditNoteLine> Lines { get; set; } = new List<CreditNoteLine>();

    public long? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public long? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public long? DeletedBy { get; set; }
    public DateTime? DeletedAt { get; set; }
    public int RowVersion { get; set; }
}
