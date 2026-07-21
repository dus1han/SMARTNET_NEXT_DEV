using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using Smartnet.Infrastructure.Pdf;

namespace Smartnet.Tests.Pdf;

/// <summary>
/// The eight QuestPDF documents, rendered.
/// </summary>
/// <remarks>
/// <para><b>What these catch.</b> QuestPDF composes a layout and throws
/// <c>DocumentLayoutException</c> when content cannot fit the space allowed for it — an over-long
/// description, a company name wider than its column, a table that will not paginate. Nothing else in
/// this project would notice: the renderers are only reached through an endpoint, and until now no test
/// called them at all. These are the six documents a customer actually receives, and the failure mode
/// is a 500 at the moment somebody presses Print.</para>
///
/// <para><b>What they do not claim.</b> A PDF's content streams are compressed, so this cannot assert
/// that a total appears in the right box — that is what <c>tools/PdfPreview</c> and a human eye are
/// for. What is asserted is the part a machine can hold: every template renders, over a realistic
/// document and over the hostile edge cases, and produces a structurally valid PDF that grows with its
/// content rather than silently truncating.</para>
///
/// <para><b>The cheque is the exception.</b> It overlays pre-printed stationery at a measured
/// 7×3.5in, so its page size is asserted directly — a template that renders beautifully on A4 would be
/// worthless.</para>
/// </remarks>
public sealed class DocumentRenderingTests
{
    /// <summary>
    /// QuestPDF refuses to render without a declared licence, and each renderer declares it in its own
    /// static constructor. These tests build the documents directly — no renderer, so no static
    /// constructor — and would otherwise fail on the licence rather than on anything about the layout.
    /// </summary>
    static DocumentRenderingTests() => QuestPDF.Settings.License = LicenseType.Community;

    // --- The documents, over realistic data -------------------------------------------------------

    public static TheoryData<string, IDocument> AllDocuments() => new()
    {
        { "invoice", new InvoiceDocument(Sample.Invoice()) },
        { "tax invoice", new TaxInvoiceDocument(Sample.TaxInvoice()) },
        { "quotation", new QuotationDocument(Sample.Quotation()) },
        { "credit note", new CreditNoteDocument(Sample.CreditNote()) },
        { "purchase order", new PurchaseOrderDocument(Sample.PurchaseOrder()) },
        { "job sheet", new JobSheetDocument(Sample.JobSheet()) },
        { "Smart Net job sheet", new SmartNetJobSheetDocument(Sample.JobSheet()) },
        { "cheque", new ChequeDocument(Sample.Cheque()) },
    };

    [Theory]
    [MemberData(nameof(AllDocuments))]
    public void Every_document_renders_to_a_valid_pdf(string name, IDocument document)
    {
        var pdf = document.GeneratePdf();

        pdf.Should().NotBeNullOrEmpty($"the {name} must produce a document");
        IsPdf(pdf).Should().BeTrue($"the {name} must be a structurally valid PDF");

        // A header and a trailer alone would pass the check above. A real page of content is kilobytes.
        pdf.Length.Should().BeGreaterThan(1_000, $"the {name} looks empty at {pdf.Length} bytes");
    }

    // --- The edge cases that actually break a layout ----------------------------------------------

    /// <summary>
    /// A description far longer than its column still renders.
    /// </summary>
    /// <remarks>
    /// The real trigger: legacy line descriptions are free text and some run to several hundred
    /// characters ("PA SYSTEM WORKS ZONE 01,02,03 Supply, wire &amp; install…"). If the column cannot
    /// wrap it, QuestPDF throws rather than clipping, and the customer's invoice will not print.
    /// </remarks>
    [Fact]
    public void A_line_description_far_longer_than_its_column_still_renders()
    {
        var monster = string.Join(" ", Enumerable.Repeat("Supply, wire and install speaker points c/w all fixing accessories", 20));

        var invoice = Sample.Invoice() with
        {
            Items = [new InvoiceItem("ITEM-1", monster, 3, 44_532m, 133_596m)],
        };

        var act = () => new InvoiceDocument(invoice).GeneratePdf();

        act.Should().NotThrow();
    }

    /// <summary>A document with no lines at all — a legacy invoice whose lines were orphaned.</summary>
    /// <remarks>
    /// Not hypothetical: three of the invoices on the dev copy have a zero total and no lines, and 105
    /// groups of lines had lost their header entirely. Printing one must not be a 500.
    /// </remarks>
    [Fact]
    public void A_document_with_no_lines_renders()
    {
        var empty = Sample.Invoice() with
        {
            Items = [],
            Subtotal = 0m,
            NetTotal = 0m,
            Total = 0m,
            Paid = 0m,
            BalanceDue = 0m,
        };

        var act = () => new InvoiceDocument(empty).GeneratePdf();

        act.Should().NotThrow();
    }

    /// <summary>Every optional field null or empty at once.</summary>
    /// <remarks>
    /// Legacy rows are `varchar` and frequently blank — no contact person, no PO number, no bank block,
    /// no logo. Each of those is a hole the layout has to tolerate, and they arrive together.
    /// </remarks>
    [Fact]
    public void A_document_missing_every_optional_field_renders()
    {
        var bare = Sample.Invoice() with
        {
            Logo = null,
            AccentColour = null,
            PoNumber = "",
            ContactPerson = "",
            ClientAddress = "",
            PreparedBy = "",
            Bank = null,
        };

        var act = () => new InvoiceDocument(bare).GeneratePdf();

        act.Should().NotThrow();
    }

    /// <summary>
    /// Enough lines to need several pages.
    /// </summary>
    /// <remarks>
    /// Pagination is where a table layout usually fails — a header that does not repeat, or a row that
    /// cannot split.
    ///
    /// <para>Asserted on <b>page count, not byte count</b>. The obvious version of this test compared
    /// file sizes and was wrong: a PDF's size is dominated by its embedded font subsets, so the
    /// three-line invoice is already ~84 KB and 120 lines adds only ~13 KB. Bytes would have passed
    /// happily while the extra lines fell off the first page; pages are the thing actually claimed.</para>
    /// </remarks>
    [Fact]
    public void A_long_document_paginates_rather_than_truncating()
    {
        var one = new InvoiceDocument(Sample.Invoice()).GeneratePdf();

        var many = Sample.Invoice() with
        {
            Items = [.. Enumerable.Range(1, 120).Select(i =>
                new InvoiceItem($"ITEM-{i}", $"Line item number {i} with a description of ordinary length", i, 1_250m, i * 1_250m))],
        };

        var manyPdf = new InvoiceDocument(many).GeneratePdf();

        IsPdf(manyPdf).Should().BeTrue();

        PageCount(one).Should().Be(1, "three lines fit on one page");
        PageCount(manyPdf).Should().BeGreaterThan(1,
            "120 lines cannot fit on one page — if they report as one, they were dropped rather than paginated");
    }

    /// <summary>Pages in the document, read from the page tree.</summary>
    /// <remarks>
    /// <c>/Type /Page</c> marks a page and <c>/Type /Pages</c> the tree node above them, so the negative
    /// lookahead matters — without it the tree node inflates every count by one.
    /// </remarks>
    private static int PageCount(byte[] pdf) =>
        Regex.Count(Encoding.ASCII.GetString(pdf), @"/Type\s*/Page(?!s)");

    /// <summary>
    /// The cheque prints at the measured stationery size, not A4.
    /// </summary>
    /// <remarks>
    /// The one template whose geometry is the whole point: it overlays a pre-printed cheque, so the page
    /// must be 7×3.5in. A4 would be a document that prints perfectly and is useless. 7in at 72pt/in is
    /// 504pt, 3.5in is 252pt — asserted against the PDF's own MediaBox.
    /// </remarks>
    [Fact]
    public void The_cheque_renders_at_the_measured_stationery_size()
    {
        var pdf = new ChequeDocument(Sample.Cheque()).GeneratePdf();

        IsPdf(pdf).Should().BeTrue();

        // MediaBox is written uncompressed in the page dictionary, so it can be read straight out.
        var text = Encoding.ASCII.GetString(pdf);
        text.Should().MatchRegex(@"/MediaBox\s*\[\s*0\s+0\s+50[34](\.\d+)?\s+25[12](\.\d+)?\s*\]",
            "the cheque overlays pre-printed stationery — 7x3.5in (504x252pt), never A4");
    }

    /// <summary>A PDF starts with %PDF- and ends with the EOF marker.</summary>
    private static bool IsPdf(byte[] bytes)
    {
        if (bytes.Length < 8)
        {
            return false;
        }

        var header = Encoding.ASCII.GetString(bytes, 0, 5) == "%PDF-";
        var tail = Encoding.ASCII.GetString(bytes, bytes.Length - 64, 64);

        return header && tail.Contains("%%EOF", StringComparison.Ordinal);
    }

    /// <summary>
    /// One realistic instance of each model, in one place.
    /// </summary>
    /// <remarks>
    /// Figures taken from a real invoice on the dev copy (SI-418) rather than invented round numbers, so
    /// the column widths are exercised against the magnitudes this business actually prints.
    /// </remarks>
    /// <summary>Internal so the page-break tests share these fixtures rather than duplicating them.</summary>
    internal static class Sample
    {
        private static readonly BankDetails Bank =
            new("Sampath Bank", "Kohuwala", "Smart Net (Pvt) Ltd", "009410008391");

        private static IReadOnlyList<InvoiceItem> Items() =>
        [
            new("ITEM-1", "Inter-M Supply of 8 Zone selector (Matrix)", 1, 427_500m, 427_500m),
            new("ITEM-2", "Inter-M Remote Station Unit", 6, 38_475m, 230_850m),
            new("ITEM-3", "PA SYSTEM WORKS ZONE 01,02,03 Supply, wire & install speaker point using 2C/0.75 Cu/PVC/PVC cable", 3, 44_532m, 133_596m),
        ];

        public static InvoiceModel Invoice() => new(
            null, "Smart Technologies", "No 5, Colombo 05 · 011 2 555 555", "#4F46E5",
            "STI-1214", "14 Jul 2026", "Credit", "014973",
            "CDEM PVT LTD", "No 100, Industrial Estate, Colombo 15", "MR SAMITH", "Dev Admin",
            Items(), 791_946m, 0m, 0m, 791_946m, 791_946m, 0m, 791_946m, Bank);

        public static TaxInvoiceModel TaxInvoice() => new(
            null, "Smart Net (Pvt) Ltd", "No 5, Colombo 05 · 011 2 555 555", "#0F766E",
            "SNI-1074", "14 Jul 2026", "14 Jul 2026",
            new TaxParty("100540013635", "Smart Net (Pvt) Ltd", "No 5, Colombo 05", "011 2 555 555"),
            new TaxParty("104578921000", "CDEM PVT LTD", "No 100, Industrial Estate, Colombo 15", "011 2 444 444"),
            "MR SAMITH", "014973", "Dev Admin",
            Items(), 791_946m, 0m, 0m, 791_946m, "VAT 18%", 142_550.28m, 934_496.28m, 0m, 934_496.28m, Bank);

        public static QuotationModel Quotation() => new(
            null, "Smart Technologies", "No 5, Colombo 05 · 011 2 555 555", "#4F46E5",
            "STQ-941", "14 Jul 2026", "CDEM PVT LTD", "No 100, Industrial Estate, Colombo 15",
            "MR SAMITH", "Dev Admin", "30 days",
            [.. Items().Select(i => new QuotationItem(i.ItemNo, i.Description, i.Quantity, i.Rate, i.Total))],
            791_946m, 0m, 0m, 791_946m, "VAT 18%", 142_550.28m, 934_496.28m, Bank);

        public static CreditNoteModel CreditNote() => new(
            null, "Smart Technologies", "No 5, Colombo 05 · 011 2 555 555", "#B91C1C",
            "STCN-88", "14 Jul 2026", "STI-1214",
            "CDEM PVT LTD", "No 100, Industrial Estate, Colombo 15", "MR SAMITH", "011 2 444 444",
            "Dev Admin", true,
            [.. Items().Select(i => new CreditNoteItem(i.Description, i.Quantity, i.Rate, i.Total))],
            133_596m, 0m, 0m, 133_596m, "VAT 18%", 24_047.28m, 157_643.28m);

        public static PurchaseOrderModel PurchaseOrder() => new(
            null, "Smart Net (Pvt) Ltd", "No 5, Colombo 05 · 011 2 555 555", "#0F766E",
            "SNPO-122", "14 Jul 2026", "BETTER CHOICE TECHNOLOGIES", "No 42, Union Place, Colombo 02",
            "Mr Perera", "011 2 333 333", "Dev Admin",
            [.. Items().Select(i => new PurchaseOrderItem(i.ItemNo, i.Description, i.Quantity, i.Rate, i.Total))],
            791_946m, 0m, 0m, 791_946m, "VAT 18%", 142_550.28m, 934_496.28m,
            "Smart Net stores, No 5, Colombo 05");

        public static JobSheetModel JobSheet() => new(
            null, "Smart Technologies", "Service & Repair", "No 5, Colombo 05 · 011 2 555 555",
            "STJOB-204", "14 Jul 2026", "PENDING",
            "CDEM PVT LTD", "No 100, Industrial Estate, Colombo 15", "011 2 444 444",
            "MR SAMITH", "Dev Admin",
            "Unit powers on but no output on zones 2 and 3. Customer reports intermittent fault since the power cut.",
            "Mainboard replaced and tested across all zones.",
            [new JobItem("Inter-M PA Amplifier", "1", "SN-88213-A"), new JobItem("Zone selector", "1", "SN-77410-B")]);

        public static ChequeModel Cheque() => new(
            "BETTER CHOICE TECHNOLOGIES", 212_700m, "20072026");
    }
}
