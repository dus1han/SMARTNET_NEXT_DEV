using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Smartnet.Infrastructure.Pdf;

/// <summary>
/// The credit note, in the house layout (see <see cref="HouseDocument"/>).
/// </summary>
/// <remarks>
/// The invoice it credits is carried in the reference block beside the note's own number, so the pair
/// can be reconciled from the face of the document. The total is labelled <c>TOTAL CREDITED</c> rather
/// than TOTAL: the figure is money going back, and a credit note that reads like an invoice is a credit
/// note somebody will eventually pay twice.
/// </remarks>
public sealed class CreditNoteDocument : HouseDocument
{
    private readonly CreditNoteModel _m;

    public CreditNoteDocument(CreditNoteModel model) : base(model.AccentColour) => _m = model;

    /// <summary>
    /// <c>TAX CREDIT NOTE</c> when the note carries VAT, <c>CREDIT NOTE</c> when it does not — the
    /// same distinction the invoice makes between <see cref="TaxInvoiceDocument"/> and
    /// <see cref="InvoiceDocument"/>, and for the same reason: it is what the customer reclaims
    /// against.
    /// </summary>
    /// <remarks>
    /// Read off <see cref="CreditNoteModel.TaxLabel"/>, which the renderer sets only for a VAT-registered
    /// company on a note that actually charges VAT. Deriving the heading from the VAT line rather than
    /// deciding it separately means the two cannot disagree — a document headed TAX CREDIT NOTE with no
    /// VAT row on it would be a contradiction, and it is the sort that gets found by an auditor rather
    /// than by us.
    ///
    /// <para>Note that this follows the <b>document</b>, not today's registration status. Smart Net
    /// registered for VAT on 2024-09-02; a credit against one of the 664 invoices raised before that
    /// charges no VAT, and reprinting it now under a TAX CREDIT NOTE heading would restate a historical
    /// supply as something it was not. Same rule as the invoice, deliberately.</para>
    /// </remarks>
    protected override string Title => _m.TaxLabel is null ? "CREDIT NOTE" : "TAX CREDIT NOTE";

    protected override byte[]? Logo => _m.Logo;

    protected override string CompanyName => _m.CompanyName;

    protected override string CompanyContact => _m.CompanyContact;

    protected override IReadOnlyList<(string Label, string Value)> References =>
    [
        ("Credit No", _m.CreditNoteNo),
        ("Date", _m.Date),
        ("Against Inv", _m.InvoiceNo),
    ];

    protected override void ComposeSections(ColumnDescriptor sections)
    {
        sections.Item().Element(c => Section(c, "Credit To", body => body.Row(row =>
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
                // Not the invoice number again — it is already in the reference block at the top, and
                // twice on one page reads as two different references.
                Field(right.Item(), "Contact Person", WithPhone(_m.ContactPerson, _m.ContactPhone));
                Field(right.Item(), "Prepared By", _m.PreparedBy);
            });
        })));

        sections.Item().Element(c => Section(c, "Items Credited", ComposeItems, paginates: true));
        // Kept whole: a totals block that breaks puts Subtotal on one page and TOTAL on the next, which is
        // the single worst place in the document to make the reader turn over.
        sections.Item().PreventPageBreak().Element(ComposeTotals);
        sections.Item().PaddingTop(8).Element(ComposeNotes);
    }

    private void ComposeItems(IContainer container)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(cols =>
            {
                cols.ConstantColumn(28);   // #
                cols.RelativeColumn();     // description
                cols.ConstantColumn(52);   // qty
                cols.ConstantColumn(78);   // rate
                cols.ConstantColumn(86);   // total
            });

            table.Header(header =>
            {
                HeaderCell(header.Cell(), "#");
                HeaderCell(header.Cell(), "Description");
                HeaderCell(header.Cell(), "Qty");
                HeaderCell(header.Cell(), "Rate");
                HeaderCell(header.Cell(), "Total");
            });

            var i = 1;
            foreach (var item in _m.Items)
            {
                BodyCell(table.Cell(), i.ToString(CultureInfo.InvariantCulture), Align.Centre);
                BodyCell(table.Cell(), item.Description);
                BodyCell(table.Cell(), Quantity(item.Quantity), Align.Centre);
                BodyCell(table.Cell(), Money(item.Rate), Align.Right);
                BodyCell(table.Cell(), Money(item.Total), Align.Right);
                i++;
            }
        });
    }

    private void ComposeTotals(IContainer container)
    {
        container.Row(row =>
        {
            row.RelativeItem();
            row.ConstantItem(268).Column(totals =>
            {
                TotalRow(totals, "Subtotal", _m.Subtotal);

                if (_m.DiscountAmount != 0m)
                {
                    var label = _m.DiscountPercent != 0m
                        ? $"Discount ({Quantity(_m.DiscountPercent)}%)"
                        : "Discount";
                    TotalRow(totals, label, -_m.DiscountAmount);
                    TotalRow(totals, "Total After Discount", _m.NetTotal);
                }

                if (_m.TaxAmount is { } tax)
                {
                    TotalRow(totals, Clean(_m.TaxLabel, "VAT"), tax);
                }

                totals.Item().PaddingTop(4).LineHorizontal(0.75f).LineColor(Accent);
                totals.Item().PaddingTop(4).Row(r =>
                {
                    // Named for what it is. "TOTAL" on a credit note reads as an amount owed.
                    r.RelativeItem().Text("TOTAL CREDITED").FontSize(10).Bold().FontColor(Accent);
                    r.ConstantItem(110).AlignRight().Text(Money(_m.Total)).FontSize(12).Bold().FontColor(Accent);
                });
            });
        });
    }

    private void ComposeNotes(IContainer container)
    {
        Section(container, "Authorisation", body => body.Column(col =>
        {
            col.Item().Row(row =>
            {
                SignLine(row.RelativeItem(), $"For {_m.CompanyName}");
                row.ConstantItem(20);
                SignLine(row.RelativeItem(), "Date");
                row.ConstantItem(20);
                row.RelativeItem();
            });

            col.Item().PaddingTop(14).Text(
                $"This credit note is issued against invoice {Clean(_m.InvoiceNo, "—")} and reduces the "
                + "amount due on it.")
                .FontSize(8.5f).Bold().FontColor(Accent);

            // Said explicitly: the same document covers a return of goods and a pure price adjustment,
            // and which one it is decides whether anybody should expect stock back.
            col.Item().PaddingTop(3).Text(
                _m.ReturnsStock
                    ? "Goods covered by this credit have been returned to stock."
                    : "This is a price adjustment only — no goods have been returned.")
                .FontSize(7.5f).FontColor(Muted).Italic();
        }));
    }
}
