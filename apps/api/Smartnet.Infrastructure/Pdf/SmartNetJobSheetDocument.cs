using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Smartnet.Infrastructure.Pdf;

/// <summary>
/// The Smart Net job sheet — a clean, premium layout distinct from <see cref="JobSheetDocument"/>: a burgundy
/// identity, an elegant title/reference masthead (no heavy band), section headers marked by a slim accent tick
/// and a hairline, white boxes, and a light horizontal-rule table. Every value is trimmed before it is drawn.
/// </summary>
public sealed class SmartNetJobSheetDocument : IDocument
{
    private const string Accent = "#6B1730";     // deep burgundy
    private const string AccentSoft = "#F5E1E7"; // burgundy tint (table header)
    private const string Ink = "#1A1A1A";
    private const string Muted = "#6B7280";
    private const string Hair = "#E3E6E5";       // hairline
    private const string White = "#FFFFFF";

    private readonly JobSheetModel _m;

    public SmartNetJobSheetDocument(JobSheetModel model) => _m = model;

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(30);
            page.DefaultTextStyle(t => t.FontSize(9.5f).FontFamily("Arial").FontColor(Ink).LineHeight(1.3f));

            page.Header().Element(ComposeHeader);
            page.Content().Element(ComposeContent);
            page.Footer().Element(ComposeFooter);
        });
    }

    private void ComposeHeader(IContainer container)
    {
        container.Column(root =>
        {
            root.Item().Row(row =>
            {
                row.RelativeItem().AlignBottom().Column(col =>
                {
                    if (_m.Logo is { Length: > 0 } logo)
                    {
                        col.Item().Height(72).AlignLeft().Image(logo).FitHeight();
                    }
                    else
                    {
                        col.Item().Text(Clean(_m.CompanyName)).FontSize(19).Bold().FontColor(Accent);
                        col.Item().Text(Clean(_m.CompanyTagline)).FontSize(9.5f).Italic().FontColor(Muted);
                    }
                });

                // Bottom-aligned so the last contact line sits on the logo's baseline (the header rule).
                row.ConstantItem(235).AlignBottom().Text(Clean(_m.CompanyContact))
                    .FontSize(8).FontColor(Muted).AlignRight().LineHeight(1.4f);
            });

            root.Item().PaddingTop(9).LineHorizontal(1).LineColor(Accent);
        });
    }

    private void ComposeContent(IContainer container)
    {
        container.PaddingTop(9).Column(col =>
        {
            // Title / reference masthead: the document title and the job identity, no heavy band.
            // Kept compact — the masthead is orientation, not content; the space belongs to the items table.
            col.Item().Row(row =>
            {
                // Centred against the three-row reference block on the right.
                row.RelativeItem().AlignMiddle().Text("JOB SHEET")
                    .FontSize(18).Bold().FontColor(Accent).LetterSpacing(0.09f);

                row.ConstantItem(215).Column(meta =>
                {
                    RefRow(meta, "Job No", _m.JobNo);
                    RefRow(meta, "Date", _m.Date);
                    RefRow(meta, "Status", _m.Status);
                });
            });

            col.Item().PaddingTop(10).Column(sections =>
            {
                sections.Spacing(9);

                sections.Item().Element(c => Section(c, "Customer & Job Details", body => body.Row(row =>
                {
                    row.RelativeItem().Column(left =>
                    {
                        left.Spacing(6);
                        Field(left.Item(), "Client", _m.ClientName);
                        Field(left.Item(), "Address", _m.ClientAddress);
                    });
                    row.ConstantItem(24);
                    row.RelativeItem().Column(right =>
                    {
                        right.Spacing(6);
                        var person = Clean(_m.ContactPerson, "—");
                        var phone = Clean(_m.ClientPhone);
                        var hasPhone = phone.Length > 0 && phone != "—";
                        Field(right.Item(), "Contact Person", hasPhone ? $"{person} ({phone})" : person);
                        Field(right.Item(), "Prepared By", _m.PreparedBy);
                    });
                })));

                sections.Item().Element(c => Section(c, "Fault Description", body => Block(body, _m.FaultDescription, 44)));
                sections.Item().Element(c => Section(c, "Equipment Received", ComposeItems, paginates: true));
                sections.Item().Element(c => Section(c, "Remarks", body => Block(body, _m.Remarks, 36)));
                sections.Item().PaddingTop(8).Element(ComposeCollection);
            });
        });
    }

    private void ComposeItems(IContainer container)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(cols =>
            {
                cols.ConstantColumn(28);
                cols.RelativeColumn();
                cols.ConstantColumn(50);
                cols.ConstantColumn(120);
            });

            table.Header(header =>
            {
                HeaderCell(header.Cell(), "#");
                HeaderCell(header.Cell(), "Item Description");
                HeaderCell(header.Cell(), "Qty");
                HeaderCell(header.Cell(), "Serial No");
            });

            var i = 1;
            foreach (var item in _m.Items)
            {
                BodyCell(table.Cell(), i.ToString(CultureInfo.InvariantCulture), center: true);
                BodyCell(table.Cell(), item.Description);
                BodyCell(table.Cell(), item.Qty, center: true);
                BodyCell(table.Cell(), item.Serial);
                i++;
            }

            for (var blank = 0; blank < 2; blank++)
            {
                BodyCell(table.Cell(), "", center: true);
                BodyCell(table.Cell(), "");
                BodyCell(table.Cell(), "", center: true);
                BodyCell(table.Cell(), "");
            }
        });
    }

    private void ComposeCollection(IContainer container)
    {
        Section(container, "Collection & Acknowledgement", body => body.Column(col =>
        {
            col.Item().Row(row =>
            {
                SignLine(row.RelativeItem(), $"Authorised Person from {_m.CompanyName}");
                row.ConstantItem(20);
                SignLine(row.RelativeItem(), "Collected By");
                row.ConstantItem(20);
                SignLine(row.RelativeItem(), "NIC");
                row.ConstantItem(20);
                SignLine(row.RelativeItem(), "Date");
            });

            col.Item().PaddingTop(14).Text("This job sheet must be presented at the time of collection.")
                .FontSize(8.5f).Bold().FontColor(Accent);
            col.Item().PaddingTop(3).Text(
                $"Goods not collected within 30 days may incur storage charges. {Clean(_m.CompanyName)} is not "
                + "responsible for any loss of data; customers are advised to keep their own backups.")
                .FontSize(7.5f).FontColor(Muted).Italic();
        }));
    }

    private void ComposeFooter(IContainer container)
    {
        container.Column(col =>
        {
            col.Item().PaddingBottom(4).LineHorizontal(0.5f).LineColor(Hair);
            col.Item().Row(row =>
            {
                row.RelativeItem().Text($"{Clean(_m.CompanyName)} · Job Sheet").FontSize(7.5f).FontColor(Muted);
                row.RelativeItem().AlignRight().Text(text =>
                {
                    text.DefaultTextStyle(t => t.FontSize(7.5f).FontColor(Muted));
                    text.Span("Page ");
                    text.CurrentPageNumber();
                    text.Span(" of ");
                    text.TotalPages();
                });
            });
        });
    }

    // --- building blocks -------------------------------------------------------------------

    /// <summary>A section header: a slim burgundy tick and the label on a light tinted bar, so it stands out
    /// clearly without the heaviness of a solid accent bar.</summary>
    /// <param name="paginates">True only for the equipment table, which may run over a page break.</param>
    /// <remarks>
    /// Kept whole by default — see <see cref="HouseDocument.Section"/> for why, and why this is
    /// PreventPageBreak rather than ShowEntire.
    /// </remarks>
    private static void Section(IContainer container, string title, Action<IContainer> body, bool paginates = false)
    {
        var section = paginates ? container : container.PreventPageBreak();

        section.Column(col =>
        {
            col.Item().Background(AccentSoft).Row(row =>
            {
                row.ConstantItem(4).Background(Accent);
                row.ConstantItem(9);
                row.RelativeItem().PaddingVertical(3).AlignMiddle()
                    .Text(title.ToUpperInvariant()).FontSize(8.5f).Bold().FontColor(Accent).LetterSpacing(0.06f);
            });
            col.Item().PaddingTop(6).Element(body);
        });
    }

    private static void RefRow(ColumnDescriptor col, string label, string value)
    {
        col.Item().PaddingBottom(1).Row(row =>
        {
            row.ConstantItem(56).AlignMiddle().Text(label.ToUpperInvariant()).FontSize(7.5f).Bold().FontColor(Muted);
            row.RelativeItem().AlignMiddle().Text(Clean(value)).FontSize(10).SemiBold().FontColor(Ink);
        });
    }

    // No trailing padding — the parent column's Spacing separates fields, so the last field does not
    // inflate the gap to the next section. Section gaps stay uniform whatever a section ends with.
    private static void Field(IContainer container, string label, string value)
    {
        container.Column(col =>
        {
            col.Item().Text(label.ToUpperInvariant()).FontSize(7).Bold().FontColor(Muted).LetterSpacing(0.03f);
            col.Item().PaddingTop(1).Text(Clean(value, "—")).FontSize(10).FontColor(Ink);
        });
    }

    private static void Block(IContainer container, string text, float minHeight)
    {
        container.Border(0.75f).BorderColor(Hair).Background(White).Padding(9).MinHeight(minHeight)
            .Text(Clean(text)).FontSize(10).FontColor(Ink);
    }

    private static string Clean(string? value, string fallback = "")
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? fallback : trimmed;
    }

    // Solid burgundy with white text — a distinct table header, clearly different from the light-tint
    // section bars. Always centred: a heading labels its column rather than lining up with its values,
    // and every document in the set centres them.
    private static void HeaderCell(IContainer cell, string text)
    {
        cell.Background(Accent).PaddingVertical(5).PaddingHorizontal(7).AlignCenter()
            .Text(text).FontSize(8.5f).Bold().FontColor(White).LetterSpacing(0.03f);
    }

    private static void BodyCell(IContainer cell, string text, bool center = false)
    {
        // Horizontal rules only (no vertical grid) — a cleaner, lighter table. Uniform row height.
        var content = cell.BorderBottom(0.5f).BorderColor(Hair).MinHeight(23).PaddingHorizontal(7).AlignMiddle();
        (center ? content.AlignCenter() : content).Text(Clean(text)).FontSize(9.5f);
    }

    private static void SignLine(IContainer container, string label)
    {
        container.Column(col =>
        {
            col.Item().PaddingTop(44).LineHorizontal(0.75f).LineColor(Ink);
            col.Item().PaddingTop(4).AlignCenter().Text(label.ToUpperInvariant()).FontSize(7.5f).Bold().FontColor(Muted);
        });
    }
}
