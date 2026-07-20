using Smartnet.Domain.Auditing;

namespace Smartnet.Domain.Settings;

/// <summary>Document types that carry a number and a company.</summary>
public static class DocumentTypes
{
    public const string Invoice = "INVOICE";
    public const string Quotation = "QUOTATION";
    public const string CreditNote = "CN";
    public const string PurchaseOrder = "PO";
    public const string SupplierInvoice = "SUPINV";
    public const string JobCard = "JOBCARD";
    public const string Payment = "PAYMENT";
    public const string Cheque = "CHEQUE";
    public const string Expense = "EXPENSE";

    public static readonly IReadOnlyList<string> All =
    [
        Invoice, Quotation, CreditNote, PurchaseOrder,
        SupplierInvoice, JobCard, Payment, Cheque, Expense,
    ];

    public static bool IsKnown(string type) => All.Contains(type);
}

/// <summary>
/// The numbering series for one document type in one company: <c>SN-INV-00042</c>.
/// </summary>
/// <remarks>
/// Replaces the legacy <c>*_seq</c> tables (<c>invoice_seq</c>, <c>quotation_seq</c>, …), which
/// allocate a number by reading the current value and writing it back — a race that hands two
/// concurrent users the same invoice number. Nothing in the schema stops that today: there is no
/// unique index on <c>invoiceno</c>, so the duplicate simply lands. (It has not happened yet:
/// checked, and there are currently zero duplicates. That is luck, not protection.)
///
/// <para><b>Phase 5 allocates from this table transactionally</b> (<c>SELECT … FOR UPDATE</c>),
/// which is what actually closes the race. This slice creates the series and lets an
/// administrator set the prefix — the thing they cannot do today without a deployment.</para>
/// </remarks>
public class DocumentSeries : IAuditable
{
    public long Id { get; set; }

    public long CompanyId { get; set; }

    /// <summary>One of <see cref="DocumentTypes"/>.</summary>
    public string DocType { get; set; } = null!;

    /// <summary>
    /// A template, not a literal — see <see cref="DocumentNumberFormat"/>. <c>STI-</c> is a prefix
    /// that never changes; <c>{YY}{MON}_SNIN_</c> renders 26JUL_SNIN_ in July and 26AUG_SNIN_ in
    /// August. Both are the same mechanism. Fully editable by an administrator.
    /// </summary>
    public string Prefix { get; set; } = string.Empty;

    /// <summary>
    /// The number the next document will take.
    /// </summary>
    /// <remarks>
    /// Continuous, and <b>never reset by a prefix change</b> — which is what the legacy data does:
    /// SNI-1556 was followed by 26JUL_SNIN_1562, straight through the rename. Resetting the counter
    /// when the prefix rolls would reissue numbers that are already on printed invoices.
    /// </remarks>
    public long NextNumber { get; set; } = 1;

    /// <summary>
    /// Zero-padding width. <b>Zero — no padding — is the legacy behaviour</b>: the numbers run
    /// STI-999, STI-1214, and the oldest is SI-10. Padding to 5 would produce STI-01215, which
    /// matches none of the 2,500 documents already printed and filed.
    /// </summary>
    public int Padding { get; set; }

    public long? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public long? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public long? DeletedBy { get; set; }
    public DateTime? DeletedAt { get; set; }
    public int RowVersion { get; set; }

    /// <summary>
    /// Formats a number in this series, as it will appear on the document.
    /// </summary>
    /// <param name="on">
    /// The document's date — not today's. A back-dated invoice must carry the prefix of the month
    /// it is dated, or its number contradicts the date printed next to it.
    /// </param>
    public string Format(long number, DateOnly on) =>
        DocumentNumberFormat.Render(Prefix, number, Padding, on);
}

/// <summary>
/// A tax rate, per company, valid over a period.
/// </summary>
/// <remarks>
/// <b>The rate is <c>decimal</c>, and every document line stores the rate that applied when it was
/// saved</b> (Phase 5). Both matter. The legacy system holds a single rate in <c>vat_validity</c>
/// and re-resolves it at print time, so reprinting a 2023 invoice today applies today's 18% to
/// lines that were taxed at whatever the rate was then. That is the bug AUDIT.md's version
/// snapshots exist to make impossible.
/// </remarks>
public class TaxRate : IAuditable, ISoftDeletable
{
    public long Id { get; set; }

    public long CompanyId { get; set; }

    /// <summary>e.g. "VAT 18%", "Zero-rated", "Exempt".</summary>
    public string Name { get; set; } = null!;

    /// <summary>A percentage: 18.0000 means 18%. Never a double.</summary>
    public decimal Percentage { get; set; }

    public DateOnly EffectiveFrom { get; set; }

    /// <summary>Null means "still in force".</summary>
    public DateOnly? EffectiveTo { get; set; }

    /// <summary>The rate offered by default on a new document line.</summary>
    /// <remarks>
    /// Default <b>for a period</b>, not for all time. Two rates may both be default provided they are
    /// never in force on the same day — that is what makes it possible to say "20% from January" in
    /// advance without disturbing the rate currently being charged. See <see cref="Overlaps"/>.
    /// </remarks>
    public bool IsDefault { get; set; }

    /// <summary>Whether this rate applies on a given day.</summary>
    public bool IsInForceOn(DateOnly date) =>
        EffectiveFrom <= date && (EffectiveTo is null || date <= EffectiveTo);

    /// <summary>
    /// Whether two rates are ever in force on the same day.
    /// </summary>
    /// <remarks>
    /// A null <see cref="EffectiveTo"/> is an open end, so it is treated as the furthest representable
    /// date rather than as "no end date" — the common case is an open-ended current rate meeting a
    /// future-dated replacement, and getting that comparison wrong is what made scheduling a rate change
    /// take down invoicing.
    /// </remarks>
    public bool Overlaps(TaxRate other) =>
        EffectiveFrom <= (other.EffectiveTo ?? DateOnly.MaxValue)
        && other.EffectiveFrom <= (EffectiveTo ?? DateOnly.MaxValue);

    public long? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public long? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public long? DeletedBy { get; set; }
    public DateTime? DeletedAt { get; set; }
    public int RowVersion { get; set; }
}
