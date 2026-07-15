using Smartnet.Domain.Settings;

namespace Smartnet.Infrastructure.Numbering;

/// <summary>
/// How the legacy system numbers documents — decoded from the live data, because it is written
/// down nowhere else.
/// </summary>
/// <remarks>
/// The legacy app allocates a number with a <b>ticket table</b>: <c>invoice_seq</c> is
/// <c>(id AUTO_INCREMENT, dt)</c>, and to get the next invoice number it INSERTs a row and takes
/// the auto-increment id. There are two per document type — the plain one and a <c>_st</c> one —
/// and they turn out to be per company:
///
/// <list type="bullet">
///   <item><c>invoice_seq_st</c> → company 1, Smart <b>T</b>echnologies. Prefix STI-. Max used
///   1214; the table's AUTO_INCREMENT is 1215.</item>
///   <item><c>invoice_seq</c> → company 2, Smart <b>N</b>et. Prefix SNI-. Max used 1570;
///   AUTO_INCREMENT 1571.</item>
/// </list>
///
/// Every document type lines up the same way, which is what confirms the reading rather than
/// merely suggesting it.
///
/// <para>The older <c>SI-</c> and <c>SQ-</c> prefixes in the data are historical: they drew from
/// the same counters and were renamed at some point. They are not separate series, and a series
/// per prefix would restart numbering at values already used.</para>
///
/// <para>Payments, cheques, expenses and supplier invoices have no counter — the legacy app does
/// not number them (a supplier invoice carries the <i>supplier's</i> number). They get no series.</para>
/// </remarks>
internal static class LegacyNumbering
{
    /// <param name="DocumentTable">Where the issued numbers live.</param>
    /// <param name="NumberColumn">The column holding e.g. "STI-1214".</param>
    /// <param name="SequenceTables">Company id → the ticket table that company's numbers came from.</param>
    internal sealed record Series(
        string DocType,
        string DocumentTable,
        string NumberColumn,
        IReadOnlyDictionary<long, string> SequenceTables);

    private const long SmartTechnologies = 1;
    private const long SmartNet = 2;

    public static readonly IReadOnlyList<Series> All =
    [
        new(DocumentTypes.Invoice, "invoice_h", "invoiceno", new Dictionary<long, string>
        {
            [SmartTechnologies] = "invoice_seq_st",
            [SmartNet] = "invoice_seq",
        }),

        new(DocumentTypes.Quotation, "quotation_h", "q_no", new Dictionary<long, string>
        {
            [SmartTechnologies] = "quotation_seq_st",
            [SmartNet] = "quotation_seq",
        }),

        new(DocumentTypes.CreditNote, "cn_h", "cnno", new Dictionary<long, string>
        {
            [SmartTechnologies] = "cn_seq_st",
            [SmartNet] = "cn_seq",
        }),

        new(DocumentTypes.PurchaseOrder, "po_h", "po_no", new Dictionary<long, string>
        {
            [SmartTechnologies] = "po_seq_st",
            [SmartNet] = "po_seq",
        }),

        new(DocumentTypes.JobCard, "jobs_m", "jobno", new Dictionary<long, string>
        {
            [SmartTechnologies] = "jobs_seq_st",
            [SmartNet] = "jobs_seq",
        }),
    ];

    /// <summary>
    /// The document number columns that can safely carry a UNIQUE index.
    /// </summary>
    /// <remarks>
    /// <b><c>quotation_h.q_no</c> is deliberately absent.</b> The live data already contains two
    /// different quotations both numbered <c>STQ-0</c> — different customers, different dates,
    /// both real. That is defect B4 (the duplicate-number race) having already happened, and the
    /// index cannot be created while it stands.
    ///
    /// <para>They are NOT renumbered here. LEGACY-DATA-POLICY.md: legacy data is left as-is,
    /// errors are prevented from cutover forward, and known defects surface in the Data Exceptions
    /// screen for the business to correct when it chooses. Rewriting a historical quotation number
    /// to make an index build is exactly the "historical remediation" that policy forbids —
    /// somebody has a PDF with STQ-0 on it.</para>
    ///
    /// <para>Add quotation_h to this list once the business has resolved the duplicate.</para>
    /// </remarks>
    public static readonly IReadOnlyList<(string Table, string Column)> UniquelyNumbered =
    [
        ("invoice_h", "invoiceno"),
        ("cn_h", "cnno"),
        ("po_h", "po_no"),
        ("jobs_m", "jobno"),
    ];
}
