using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using QuestPDF.Fluent;
using Smartnet.Infrastructure.Pdf;

namespace Smartnet.Tests.Pdf;

/// <summary>
/// Sections are kept whole across a page break — and keeping them whole does not break rendering.
/// </summary>
/// <remarks>
/// <para>
/// <b>What these can and cannot assert.</b> Content streams are compressed, so a test cannot read where
/// on the page a heading landed; proving "the heading and its detail share a page" by inspection is what
/// <c>tools/PdfPreview</c> and a human eye are for. What a test <i>can</i> hold is the failure mode the
/// fix introduces, which is the more dangerous one: <c>PreventPageBreak</c> asks the layout engine to fit
/// a block on one page, and a block that cannot fit is exactly how a PDF renderer throws or loops.
/// </para>
/// <para>
/// So these sweep the item count across the page boundary and push a single section past a full page,
/// which is where a keep-together rule fails if it is going to. They would all have passed before the
/// change; they exist so that the change cannot have made any document unrenderable.
/// </para>
/// </remarks>
public sealed class SectionPageBreakTests
{
    private static int PageCount(byte[] pdf) =>
        Regex.Count(Encoding.ASCII.GetString(pdf), @"/Type\s*/Page(?!s)");

    private static bool IsPdf(byte[] bytes) =>
        bytes.Length > 4 && Encoding.ASCII.GetString(bytes, 0, 5) == "%PDF-";

    private static InvoiceModel InvoiceWith(int lines) =>
        DocumentRenderingTests.Sample.Invoice() with
        {
            Items = [.. Enumerable.Range(1, lines).Select(i =>
                new InvoiceItem(
                    $"ITEM-{i}",
                    $"Line item number {i} with a description of ordinary length",
                    i,
                    1_250m,
                    i * 1_250m))],
        };

    /// <summary>
    /// Every item count from one line to well past a full page still renders.
    /// </summary>
    /// <remarks>
    /// The boundary cases are the point. Somewhere in this range the totals block, the payment details and
    /// the receipt stop fitting under the table and have to move — one line at a time, so whichever count
    /// lands a section exactly on the margin is covered rather than hoped over.
    /// </remarks>
    [Fact]
    public void Every_item_count_across_the_page_boundary_still_renders()
    {
        foreach (var lines in Enumerable.Range(1, 45))
        {
            var pdf = new InvoiceDocument(InvoiceWith(lines)).GeneratePdf();

            IsPdf(pdf).Should().BeTrue($"an invoice of {lines} lines must render");
            PageCount(pdf).Should().BeGreaterThan(0, $"an invoice of {lines} lines must have a page");
        }
    }

    /// <summary>
    /// Page count never goes backwards as lines are added.
    /// </summary>
    /// <remarks>
    /// A keep-together rule moves content forward, so it can legitimately add a page. What it must never
    /// do is lose one: a count that drops as lines are added means content stopped being drawn.
    /// </remarks>
    [Fact]
    public void Adding_lines_never_reduces_the_page_count()
    {
        var counts = Enumerable.Range(1, 40)
            .Select(lines => PageCount(new InvoiceDocument(InvoiceWith(lines)).GeneratePdf()))
            .ToList();

        counts.Should().BeInAscendingOrder();
        counts[^1].Should().BeGreaterThan(counts[0], "forty lines take more room than one");
    }

    /// <summary>
    /// A single section longer than a page still renders, rather than failing to lay out.
    /// </summary>
    /// <remarks>
    /// This is the case that decided <c>PreventPageBreak</c> over <c>ShowEntire</c>. ShowEntire demands
    /// the content fit on one page and fails the layout when it cannot — and a fault description is free
    /// text with no length limit, so the failing version would be one long paragraph away from a job sheet
    /// that cannot be produced at all. PreventPageBreak keeps a section together where it fits and lets it
    /// flow where it does not.
    /// </remarks>
    [Fact]
    public void A_section_longer_than_a_whole_page_still_renders()
    {
        var essay = string.Join(
            " ",
            Enumerable.Repeat(
                "The unit powers on but produces no output on zones two and three, and the customer "
                + "reports an intermittent fault dating from the power cut.",
                80));

        var model = DocumentRenderingTests.Sample.JobSheet() with { FaultDescription = essay };

        var pdf = new JobSheetDocument(model).GeneratePdf();

        IsPdf(pdf).Should().BeTrue();
        PageCount(pdf).Should().BeGreaterThan(1, "a section that long cannot fit on one page");
    }

    /// <summary>The same, on the house-styled documents that share HouseDocument.Section.</summary>
    [Fact]
    public void The_other_templates_survive_the_boundary_too()
    {
        var lines = Enumerable.Range(1, 30)
            .Select(i => new InvoiceItem($"ITEM-{i}", $"Line {i}", i, 1_000m, i * 1_000m))
            .ToList();

        var quotation = DocumentRenderingTests.Sample.Quotation() with
        {
            Items = [.. lines.Select(i => new QuotationItem(i.ItemNo, i.Description, i.Quantity, i.Rate, i.Total))],
        };
        var purchaseOrder = DocumentRenderingTests.Sample.PurchaseOrder() with
        {
            Items = [.. lines.Select(i => new PurchaseOrderItem(i.ItemNo, i.Description, i.Quantity, i.Rate, i.Total))],
        };
        var creditNote = DocumentRenderingTests.Sample.CreditNote() with
        {
            Items = [.. lines.Select(i => new CreditNoteItem(i.Description, i.Quantity, i.Rate, i.Total))],
        };
        var taxInvoice = DocumentRenderingTests.Sample.TaxInvoice() with { Items = lines };

        IsPdf(new QuotationDocument(quotation).GeneratePdf()).Should().BeTrue();
        IsPdf(new PurchaseOrderDocument(purchaseOrder).GeneratePdf()).Should().BeTrue();
        IsPdf(new CreditNoteDocument(creditNote).GeneratePdf()).Should().BeTrue();
        IsPdf(new TaxInvoiceDocument(taxInvoice).GeneratePdf()).Should().BeTrue();
    }
}
