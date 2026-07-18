using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Smartnet.Infrastructure.Pdf;

/// <summary>
/// The house layout every printed document shares: the company masthead, the section headers, the
/// detail table, the signature lines and the footer.
/// </summary>
/// <remarks>
/// <b>Written once because copying it did not work.</b> The quotation began as its own file with the job
/// sheet's building blocks re-typed by hand, and drifted in six places nobody could see until two
/// documents were held side by side — a 72pt label column against 56, a 21pt row against 23, a computed
/// section tint against the chosen one. Each was invisible alone and obvious together. A document that
/// derives from this cannot drift, because there is nothing to keep in step.
///
/// <para>What a subclass supplies is what actually differs: the title, the reference rows, and the
/// sections in between. Everything a reader would recognise as "our documents" lives here.</para>
///
/// <para><b>Two header layouts, chosen by company.</b> The two job sheets are not one design in two
/// colours — Smart Net's sets the contact beside the logo and the title in a masthead below the rule,
/// while the default one stacks the contact under the logo and puts the title in a bordered box. A
/// company's documents follow whichever its job sheet uses, so everything it sends matches.</para>
/// </remarks>
public abstract class HouseDocument : IDocument
{
    protected const string Ink = "#1A1A1A";
    protected const string Muted = "#6B7280";
    protected const string Hair = "#E3E6E5";
    protected const string White = "#FFFFFF";

    protected string Accent { get; }

    protected string AccentSoft { get; }

    /// <summary>True for the Smart Net masthead layout, false for the boxed-header one.</summary>
    protected bool MastheadLayout { get; }

    protected HouseDocument(string? accentColour)
    {
        Accent = IsColour(accentColour) ? accentColour!.Trim() : CompanyTheme.DefaultAccent;
        AccentSoft = CompanyTheme.TintOf(Accent);
        MastheadLayout = string.Equals(Accent, CompanyTheme.SmartNetAccent, StringComparison.OrdinalIgnoreCase);
    }

    // --- what a document must supply ---------------------------------------------------------

    /// <summary>The word across the top — "QUOTATION", "PURCHASE ORDER".</summary>
    protected abstract string Title { get; }

    protected abstract byte[]? Logo { get; }

    protected abstract string CompanyName { get; }

    protected abstract string CompanyContact { get; }

    /// <summary>The document's reference rows — its number, date, and whatever else identifies it.</summary>
    protected abstract IReadOnlyList<(string Label, string Value)> References { get; }

    /// <summary>Everything between the header and the footer.</summary>
    protected abstract void ComposeSections(ColumnDescriptor sections);

    // --- the layout --------------------------------------------------------------------------

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
        if (MastheadLayout)
        {
            ComposeMastheadHeader(container);
            return;
        }

        ComposeBoxedHeader(container);
    }

    /// <summary>Logo left, contact right, an accent rule under both.</summary>
    private void ComposeMastheadHeader(IContainer container)
    {
        container.Column(root =>
        {
            root.Item().Row(row =>
            {
                row.RelativeItem().AlignBottom().Column(col =>
                {
                    if (Logo is { Length: > 0 } logo)
                    {
                        col.Item().Height(72).AlignLeft().Image(logo).FitHeight();
                    }
                    else
                    {
                        col.Item().Text(Clean(CompanyName)).FontSize(19).Bold().FontColor(Accent);
                    }
                });

                row.ConstantItem(235).AlignBottom().Text(Clean(CompanyContact))
                    .FontSize(8).FontColor(Muted).AlignRight().LineHeight(1.4f);
            });

            root.Item().PaddingTop(9).LineHorizontal(1).LineColor(Accent);
        });
    }

    /// <summary>
    /// Logo with the contact stacked beneath, and the title and references in a bordered box on the
    /// right. The title lives in the box, so no masthead follows.
    /// </summary>
    private void ComposeBoxedHeader(IContainer container)
    {
        container.Column(root =>
        {
            root.Item().Row(row =>
            {
                row.RelativeItem().AlignMiddle().Column(col =>
                {
                    if (Logo is { Length: > 0 } logo)
                    {
                        col.Item().Height(84).AlignLeft().Image(logo).FitHeight();
                    }
                    else
                    {
                        col.Item().Text(Clean(CompanyName)).FontSize(19).Bold().FontColor(Accent);
                    }

                    col.Item().PaddingTop(5).Text(Clean(CompanyContact)).FontSize(8).FontColor(Muted);
                });

                row.ConstantItem(190).Border(1).BorderColor(Accent).Column(box =>
                {
                    box.Item().Background(Accent).PaddingVertical(6).AlignCenter()
                        .Text(Title).FontSize(13).Bold().FontColor(White).LetterSpacing(0.08f);

                    box.Item().PaddingHorizontal(10).PaddingVertical(9).Column(meta =>
                    {
                        meta.Spacing(6);
                        foreach (var (label, value) in References)
                        {
                            MetaRow(meta, label, value);
                        }
                    });
                });
            });

            root.Item().PaddingTop(8).LineHorizontal(1.5f).LineColor(Accent);
        });
    }

    private void ComposeContent(IContainer container)
    {
        container.PaddingTop(9).Column(col =>
        {
            // Only the masthead layout prints a title band here — the boxed header already carries the
            // title and references, and repeating them would print them twice.
            if (MastheadLayout)
            {
                col.Item().Row(row =>
                {
                    row.RelativeItem().AlignMiddle().Text(Title)
                        .FontSize(18).Bold().FontColor(Accent).LetterSpacing(0.09f);

                    row.ConstantItem(215).Column(meta =>
                    {
                        foreach (var (label, value) in References)
                        {
                            RefRow(meta, label, value);
                        }
                    });
                });
            }

            col.Item().PaddingTop(MastheadLayout ? 10 : 0).Column(sections =>
            {
                sections.Spacing(9);
                ComposeSections(sections);
            });
        });
    }

    private void ComposeFooter(IContainer container)
    {
        container.Column(col =>
        {
            col.Item().PaddingBottom(4).LineHorizontal(0.5f).LineColor(Hair);
            col.Item().Row(row =>
            {
                row.RelativeItem().Text($"{Clean(CompanyName)} · {FooterName}").FontSize(7.5f).FontColor(Muted);
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

    /// <summary>How the document names itself in the footer — title case, as "Job Sheet" does.</summary>
    protected virtual string FooterName =>
        CultureInfo.InvariantCulture.TextInfo.ToTitleCase(Title.ToLowerInvariant());

    // --- building blocks ---------------------------------------------------------------------

    protected enum Align { Left, Centre, Right }

    /// <summary>A section header: a slim accent tick and the label on a tinted bar.</summary>
    protected void Section(IContainer container, string title, Action<IContainer> body)
    {
        container.Column(col =>
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

    /// <summary>A reference row in the masthead.</summary>
    protected static void RefRow(ColumnDescriptor col, string label, string value)
    {
        col.Item().PaddingBottom(1).Row(row =>
        {
            row.ConstantItem(56).AlignMiddle().Text(label.ToUpperInvariant()).FontSize(7.5f).Bold().FontColor(Muted);
            row.RelativeItem().AlignMiddle().Text(Clean(value)).FontSize(10).SemiBold().FontColor(Ink);
        });
    }

    /// <summary>
    /// A reference row inside the boxed header. The label column is 62pt where the job sheet's is 50pt,
    /// deliberately: "QUOTE NO" and "VALID FOR" are longer than "JOB NO" and "STATUS", and at 50pt they
    /// ran up against their values with a quarter of the breathing space. This reproduces the job
    /// sheet's appearance rather than its number.
    /// </summary>
    protected static void MetaRow(ColumnDescriptor col, string label, string value)
    {
        col.Item().Row(row =>
        {
            row.ConstantItem(62).AlignMiddle().Text(label.ToUpperInvariant()).FontSize(7.5f).Bold().FontColor(Muted);
            row.RelativeItem().AlignMiddle().Text(Clean(value)).FontSize(10).SemiBold().FontColor(Ink);
        });
    }

    /// <summary>A labelled value inside a section. Spacing comes from the parent column.</summary>
    protected static void Field(IContainer container, string label, string value)
    {
        container.Column(col =>
        {
            col.Item().Text(label.ToUpperInvariant()).FontSize(7).Bold().FontColor(Muted).LetterSpacing(0.03f);
            col.Item().PaddingTop(1).Text(Clean(value, "—")).FontSize(10).FontColor(Ink);
        });
    }

    /// <summary>
    /// A column heading. Always centred, whatever its column's body alignment.
    /// </summary>
    /// <remarks>
    /// Headings label the column rather than line up with its values, and centring them reads as a
    /// header band instead of as a first row. The body keeps its own alignment — money right, counts
    /// centred, text left — which is what makes figures comparable down the column.
    /// </remarks>
    protected void HeaderCell(IContainer cell, string text)
    {
        cell.Background(Accent).PaddingVertical(5).PaddingHorizontal(7).AlignCenter()
            .Text(text).FontSize(8.5f).Bold().FontColor(White).LetterSpacing(0.03f);
    }

    protected static void BodyCell(IContainer cell, string text, Align align = Align.Left)
    {
        // Horizontal rules only — a lighter table than a full grid. Uniform row height.
        var content = cell.BorderBottom(0.5f).BorderColor(Hair).MinHeight(23).PaddingHorizontal(7).AlignMiddle();
        Aligned(content, align).Text(Clean(text)).FontSize(9.5f);
    }

    protected static void TotalRow(ColumnDescriptor col, string label, decimal value)
    {
        col.Item().PaddingBottom(2).Row(row =>
        {
            row.RelativeItem().AlignRight().PaddingRight(12).Text(label).FontSize(9.5f).FontColor(Muted);
            row.ConstantItem(110).AlignRight().Text(Money(value)).FontSize(9.5f).FontColor(Ink);
        });
    }

    protected static void SignLine(IContainer container, string label)
    {
        container.Column(col =>
        {
            col.Item().PaddingTop(44).LineHorizontal(0.75f).LineColor(Ink);
            col.Item().PaddingTop(4).AlignCenter().Text(label.ToUpperInvariant()).FontSize(7.5f).Bold().FontColor(Muted);
        });
    }

    protected static IContainer Aligned(IContainer container, Align align) => align switch
    {
        Align.Centre => container.AlignCenter(),
        Align.Right => container.AlignRight(),
        _ => container,
    };

    /// <summary>Money with thousands separators and two decimals — formatted here, not upstream.</summary>
    protected static string Money(decimal value) => value.ToString("N2", CultureInfo.InvariantCulture);

    /// <summary>A quantity without trailing zeros: "2" not "2.00", but "1.5" kept.</summary>
    protected static string Quantity(decimal value) =>
        value == decimal.Truncate(value)
            ? decimal.Truncate(value).ToString("0", CultureInfo.InvariantCulture)
            : value.ToString("0.##", CultureInfo.InvariantCulture);

    protected static string Clean(string? value, string fallback = "")
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? fallback : trimmed;
    }

    /// <summary>
    /// A contact as "Name (telephone)" — or just the name when no number is on file, and just the number
    /// when no name is. Whoever picks the document up needs to know who to ring, and an empty bracket is
    /// worse than no bracket.
    /// </summary>
    protected static string WithPhone(string? name, string? phone)
    {
        var person = Clean(name);
        var number = Clean(phone);

        if (number.Length == 0)
        {
            return person;
        }

        return person.Length == 0 ? number : $"{person} ({number})";
    }

    /// <summary>A #RRGGBB colour, the only form the templates accept.</summary>
    private static bool IsColour(string? colour) =>
        colour is not null
        && colour.Trim() is { Length: 7 } c
        && c[0] == '#'
        && c[1..].All(Uri.IsHexDigit);
}
