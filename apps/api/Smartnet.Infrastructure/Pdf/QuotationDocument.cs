using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Smartnet.Infrastructure.Pdf;

/// <summary>
/// The quotation, in the house layout (see <see cref="HouseDocument"/>) — what differs from the other
/// documents is its content, not its appearance.
/// </summary>
/// <remarks>
/// <b>One template, not the legacy pair.</b> <c>Quotation_SN</c> and <c>Quotation_ST</c> differed only
/// because one company is VAT-registered and the other is not. Here the VAT rows appear when
/// <see cref="QuotationModel.TaxAmount"/> is set and the payment block when bank details are on file,
/// so both companies — and any future one — are served by this file.
/// </remarks>
public sealed class QuotationDocument : HouseDocument
{
    private readonly QuotationModel _m;

    public QuotationDocument(QuotationModel model) : base(model.AccentColour) => _m = model;

    protected override string Title => "QUOTATION";

    protected override byte[]? Logo => _m.Logo;

    protected override string CompanyName => _m.CompanyName;

    protected override string CompanyContact => _m.CompanyContact;

    protected override IReadOnlyList<(string Label, string Value)> References
    {
        get
        {
            // "Quote No", not "Quotation No": the reference labels share the job sheet's column width and
            // the longer wording wraps in it. The title already says QUOTATION.
            var rows = new List<(string, string)>
            {
                ("Quote No", _m.QuotationNo),
                ("Date", _m.Date),
            };

            if (Validity(_m.Validity).Length > 0)
            {
                rows.Add(("Valid For", Validity(_m.Validity)));
            }

            return rows;
        }
    }

    protected override void ComposeSections(ColumnDescriptor sections)
    {
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

        sections.Item().Element(c => Section(c, "Items Quoted", ComposeItems));
        sections.Item().Element(ComposeTotals);

        if (_m.Bank is not null)
        {
            sections.Item().Element(c => Section(c, "Payment Details", ComposeBank));
        }

        sections.Item().PaddingTop(8).Element(ComposeAcceptance);
    }

    private void ComposeItems(IContainer container)
    {
        container.Table(table =>
        {
            // No item-code column: it is an internal reference, and this document is read by the
            // customer. The space it took goes to the description.
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

    /// <summary>
    /// The money column, right-aligned under the table's Total column so the figures line up with the
    /// lines they come from. VAT rows appear only for a VAT-registered company.
    /// </summary>
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
                    r.RelativeItem().Text("TOTAL").FontSize(10).Bold().FontColor(Accent);
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
                Field(left.Item(), "Bank", Join(bank.BankName, bank.Branch));
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

    private void ComposeAcceptance(IContainer container)
    {
        Section(container, "Acceptance", body => body.Column(col =>
        {
            col.Item().Row(row =>
            {
                SignLine(row.RelativeItem(), $"For {_m.CompanyName}");
                row.ConstantItem(20);
                SignLine(row.RelativeItem(), "Accepted By");
                row.ConstantItem(20);
                SignLine(row.RelativeItem(), "Date");
            });

            col.Item().PaddingTop(14).Text(
                Validity(_m.Validity) is { Length: > 0 } validity
                    ? $"This quotation is valid for {validity} from the date above."
                    : "Prices quoted are subject to change without prior notice.")
                .FontSize(8.5f).Bold().FontColor(Accent);

            col.Item().PaddingTop(3).Text(
                "Goods and services remain subject to availability at the time of order. "
                + "This quotation is an offer, not an invoice — no charge arises until an order is placed.")
                .FontSize(7.5f).FontColor(Muted).Italic();
        }));
    }

    /// <summary>
    /// The validity, with its unit. <c>q_valid</c> stores a bare number and that number is a count of
    /// days, so the unit is supplied here rather than left for the reader to guess. A value that already
    /// carries its own wording ("30 Days", "Until stocks last") is printed as written.
    /// </summary>
    private static string Validity(string? raw)
    {
        var value = Clean(raw);

        if (value.Length == 0 || value.Any(char.IsLetter))
        {
            return value;
        }

        return value == "1" ? "1 day" : $"{value} days";
    }

    private static string Join(params string?[] parts) =>
        string.Join(" — ", parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()));
}
