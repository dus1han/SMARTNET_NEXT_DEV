using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Smartnet.Infrastructure.Pdf;

/// <summary>What a proforma invoice renders from.</summary>
/// <param name="TaxAmount">Null when the company is not VAT-registered, which omits the VAT row.</param>
public sealed record ProformaModel(
    byte[]? Logo,
    string CompanyName,
    string CompanyContact,
    string? AccentColour,
    string ProformaNo,
    string QuotationNo,
    string Date,
    string ClientName,
    string ClientAddress,
    string ContactPerson,
    string PreparedBy,
    string Validity,
    IReadOnlyList<QuotationItem> Items,
    decimal Subtotal,
    decimal DiscountPercent,
    decimal DiscountAmount,
    decimal NetTotal,
    string? TaxLabel,
    decimal? TaxAmount,
    decimal Total,
    BankDetails? Bank);

/// <summary>
/// The proforma invoice, in the house layout (see <see cref="HouseDocument"/>).
/// </summary>
/// <remarks>
/// Laid out like an invoice — the customer is asked to pay it — but it is <b>not a tax invoice</b>, and it
/// says so plainly: a buyer cannot reclaim VAT against it, and the tax invoice follows when it is converted.
/// The payment block is headed as what to pay into, and the quotation's acceptance block is gone: the offer
/// has already been accepted by the time a proforma exists.
/// </remarks>
public sealed class ProformaDocument : HouseDocument
{
    private const string NoticeRed = "#B91C1C";

    private readonly ProformaModel _m;

    public ProformaDocument(ProformaModel model) : base(model.AccentColour) => _m = model;

    protected override string Title => "PROFORMA INVOICE";

    protected override string FooterName => "Proforma Invoice";

    protected override byte[]? Logo => _m.Logo;

    protected override string CompanyName => _m.CompanyName;

    protected override string CompanyContact => _m.CompanyContact;

    protected override IReadOnlyList<(string Label, string Value)> References
    {
        get
        {
            var rows = new List<(string, string)>
            {
                // "PI No", not "Proforma No": the longer label wraps in the masthead's reference column.
                ("PI No", _m.ProformaNo),
                ("Date", _m.Date),
                ("Quote Ref", _m.QuotationNo),
            };

            if (Clean(_m.Validity).Length > 0)
            {
                rows.Add(("Valid For", Clean(_m.Validity)));
            }

            return rows;
        }
    }

    protected override void ComposeSections(ColumnDescriptor sections)
    {
        sections.Item().Element(ComposeNotice);

        sections.Item().Element(c => Section(c, "Bill To", body => body.Row(row =>
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
                Field(right.Item(), "Contact Person", _m.ContactPerson);
                Field(right.Item(), "Prepared By", _m.PreparedBy);
            });
        })));

        sections.Item().Element(c => Section(c, "Items", ComposeItems, paginates: true));
        // Kept whole, so Subtotal and TOTAL never land on different pages.
        sections.Item().PreventPageBreak().Element(ComposeTotals);

        if (_m.Bank is not null)
        {
            sections.Item().Element(c => Section(c, "Payment Details", ComposeBank));
        }

        sections.Item().PaddingTop(4).Text(
                "Please quote the proforma number with your payment. A tax invoice will be issued on payment.")
            .FontSize(8.5f).Bold().FontColor(Accent);
    }

    /// <summary>The line that stops this being mistaken for — or claimed against as — a tax invoice.</summary>
    private static void ComposeNotice(IContainer container)
    {
        container.Border(1).BorderColor(NoticeRed).Background("#FEF2F2").PaddingVertical(5).PaddingHorizontal(9)
            .Text(text =>
            {
                text.Span("THIS IS NOT A TAX INVOICE. ").FontSize(9).Bold().FontColor(NoticeRed);
                text.Span("A tax invoice will be issued upon payment.").FontSize(8.5f).FontColor(NoticeRed);
            });
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
                    r.RelativeItem().Text("AMOUNT DUE").FontSize(10).Bold().FontColor(Accent);
                    r.ConstantItem(110).AlignRight().Text(Money(_m.Total)).FontSize(12).Bold().FontColor(Accent);
                });
            });
        });
    }

    private void ComposeBank(IContainer container)
    {
        var bank = _m.Bank!;

        container.Row(row =>
        {
            row.RelativeItem().Column(left =>
            {
                left.Spacing(6);
                Field(left.Item(), "Bank", string.Join(" — ", new[] { bank.BankName, bank.Branch }
                    .Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim())));
                if (Clean(bank.AccountName).Length > 0) Field(left.Item(), "Account Name", bank.AccountName!);
            });
            row.ConstantItem(24);
            row.RelativeItem().Column(right =>
            {
                right.Spacing(6);
                if (Clean(bank.AccountNumber).Length > 0) Field(right.Item(), "Account Number", bank.AccountNumber!);
            });
        });
    }
}
