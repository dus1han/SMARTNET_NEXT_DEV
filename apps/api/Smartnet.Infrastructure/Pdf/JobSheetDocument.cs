using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Smartnet.Infrastructure.Pdf;

/// <summary>
/// The Smart Technologies job sheet (Phase 8, PDF templates — the Job_ST variant). A4 portrait, deep-navy
/// accent: logo masthead, the job identity box, customer &amp; job details, fault description, equipment
/// received, remarks, and a collection/acknowledgement footer. Every value is trimmed before it is drawn.
/// </summary>
public sealed class JobSheetDocument : IDocument
{
    private const string Accent = "#1F3A5F"; // deep navy
    private const string Ink = "#111827";
    private const string Muted = "#6B7280";
    private const string Line = "#D0D5DD";
    private const string Soft = "#F8FAFC";
    private const string White = "#FFFFFF";

    private readonly JobSheetModel _m;

    public JobSheetDocument(JobSheetModel model) => _m = model;

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(28);
            page.DefaultTextStyle(t => t.FontSize(9.5f).FontFamily("Arial").FontColor(Ink).LineHeight(1.25f));

            page.Header().Element(ComposeHeader);
            page.Content().PaddingTop(12).Element(ComposeContent);
            page.Footer().Element(ComposeFooter);
        });
    }

    private void ComposeHeader(IContainer container)
    {
        container.Column(root =>
        {
            root.Item().Row(row =>
            {
                row.RelativeItem().AlignMiddle().Column(col =>
                {
                    if (_m.Logo is { Length: > 0 } logo)
                    {
                        col.Item().Height(84).AlignLeft().Image(logo).FitHeight();
                    }
                    else
                    {
                        col.Item().Text(Clean(_m.CompanyName)).FontSize(19).Bold().FontColor(Accent);
                        col.Item().Text(Clean(_m.CompanyTagline)).FontSize(9.5f).Italic().FontColor(Muted);
                    }

                    col.Item().PaddingTop(5).Text(Clean(_m.CompanyContact)).FontSize(8).FontColor(Muted);
                });

                row.ConstantItem(190).Border(1).BorderColor(Accent).Column(box =>
                {
                    box.Item().Background(Accent).PaddingVertical(6).AlignCenter()
                        .Text("JOB SHEET").FontSize(13).Bold().FontColor(White).LetterSpacing(0.08f);
                    box.Item().PaddingHorizontal(10).PaddingVertical(9).Column(meta =>
                    {
                        meta.Spacing(6);
                        MetaRow(meta, "Job No", _m.JobNo);
                        MetaRow(meta, "Date", _m.Date);
                        MetaRow(meta, "Status", _m.Status);
                    });
                });
            });

            root.Item().PaddingTop(8).LineHorizontal(1.5f).LineColor(Accent);
        });
    }

    private void ComposeContent(IContainer container)
    {
        container.Column(col =>
        {
            col.Spacing(12);

            col.Item().Element(c => Section(c, "Customer & Job Details", body => body.Row(row =>
            {
                row.RelativeItem().Column(left =>
                {
                    Field(left.Item(), "Client", _m.ClientName);
                    Field(left.Item(), "Address", _m.ClientAddress);
                });
                row.ConstantItem(20);
                row.RelativeItem().Column(right =>
                {
                    var person = Clean(_m.ContactPerson, "—");
                    var phone = Clean(_m.ClientPhone);
                    var hasPhone = phone.Length > 0 && phone != "—";
                    Field(right.Item(), "Contact Person", hasPhone ? $"{person} ({phone})" : person);
                    Field(right.Item(), "Prepared By", _m.PreparedBy);
                });
            })));

            col.Item().Element(c => Section(c, "Fault Description", body => Block(body, _m.FaultDescription, 46)));
            col.Item().Element(c => Section(c, "Equipment Received", ComposeItems));
            col.Item().Element(c => Section(c, "Remarks", body => Block(body, _m.Remarks, 38)));
            col.Item().PaddingTop(6).Element(ComposeCollection);
        });
    }

    private void ComposeItems(IContainer container)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(cols =>
            {
                cols.ConstantColumn(26);
                cols.RelativeColumn();
                cols.ConstantColumn(48);
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
                row.ConstantItem(18);
                SignLine(row.RelativeItem(), "Collected By");
                row.ConstantItem(18);
                SignLine(row.RelativeItem(), "NIC");
                row.ConstantItem(18);
                SignLine(row.RelativeItem(), "Date");
            });

            col.Item().PaddingTop(12).Text("This job sheet must be presented at the time of collection.")
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
            col.Item().PaddingBottom(4).LineHorizontal(0.5f).LineColor(Line);
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

    private static void Section(IContainer container, string title, Action<IContainer> body)
    {
        container.Column(col =>
        {
            col.Item().PaddingBottom(4).BorderBottom(1).BorderColor(Accent)
                .Text(title.ToUpperInvariant()).FontSize(9).Bold().FontColor(Accent);
            col.Item().PaddingTop(6).Element(body);
        });
    }

    private static void Field(IContainer container, string label, string value)
    {
        container.PaddingBottom(6).Column(col =>
        {
            col.Item().Text(label.ToUpperInvariant()).FontSize(7).Bold().FontColor(Muted);
            col.Item().Text(Clean(value, "—")).FontSize(10).FontColor(Ink);
        });
    }

    private static void Block(IContainer container, string text, float minHeight)
    {
        container.Border(1).BorderColor(Line).Background(Soft).Padding(8).MinHeight(minHeight)
            .Text(Clean(text)).FontSize(10).FontColor(Ink);
    }

    private static string Clean(string? value, string fallback = "")
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? fallback : trimmed;
    }

    private static void MetaRow(ColumnDescriptor col, string label, string value)
    {
        col.Item().Row(row =>
        {
            row.ConstantItem(50).AlignMiddle().Text(label.ToUpperInvariant()).FontSize(7.5f).Bold().FontColor(Muted);
            row.RelativeItem().AlignMiddle().Text(Clean(value)).FontSize(10).SemiBold().FontColor(Ink);
        });
    }

    // Always centred: a heading labels its column rather than lining up with its values, and every
    // document in the set centres them.
    private static void HeaderCell(IContainer cell, string text)
    {
        cell.Background(Accent).PaddingVertical(4).PaddingHorizontal(6).AlignCenter()
            .Text(text).FontSize(8.5f).Bold().FontColor(White);
    }

    private static void BodyCell(IContainer cell, string text, bool center = false)
    {
        var content = cell.Border(0.5f).BorderColor(Line).MinHeight(22).PaddingHorizontal(6).AlignMiddle();
        (center ? content.AlignCenter() : content).Text(Clean(text)).FontSize(9.5f);
    }

    private static void SignLine(IContainer container, string label)
    {
        container.Column(col =>
        {
            col.Item().PaddingTop(44).LineHorizontal(1).LineColor(Ink);
            col.Item().PaddingTop(4).AlignCenter().Text(label.ToUpperInvariant()).FontSize(7.5f).Bold().FontColor(Muted);
        });
    }
}
