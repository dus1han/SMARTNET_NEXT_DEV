using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Smartnet.Infrastructure.Pdf;

/// <summary>
/// The tax invoice — the <c>Invoice_SN_TAX</c> replacement, in the house layout
/// (see <see cref="HouseDocument"/>) with the two departures the document legally needs.
/// </summary>
/// <remarks>
/// <b>The title is centred and the references sit beneath it.</b> Every other document puts its title on
/// the left with its number and date in a column beside it. A tax invoice cannot: the VAT rules want the
/// supplier's and the purchaser's registration details set out side by side, and fourteen fields do not
/// fit in a corner. So the title becomes a centred band and the parties form a block under it — which is
/// also how the legacy report set it out, and how the people who read these documents expect them.
///
/// <para>Everything else is the house layout unchanged: the Smart Net masthead, the same table, the same
/// totals column, the same receipt block.</para>
/// </remarks>
public sealed class TaxInvoiceDocument : HouseDocument
{
    private readonly TaxInvoiceModel _m;

    public TaxInvoiceDocument(TaxInvoiceModel model) : base(model.AccentColour) => _m = model;

    protected override string Title => "TAX INVOICE";

    protected override bool CentredTitle => true;

    protected override byte[]? Logo => _m.Logo;

    protected override string CompanyName => _m.CompanyName;

    protected override string CompanyContact => _m.CompanyContact;

    /// <summary>Empty — the references are in the party block, which <see cref="CentredTitle"/> requires.</summary>
    protected override IReadOnlyList<(string Label, string Value)> References => [];

    protected override void ComposeSections(ColumnDescriptor sections)
    {
        // The VAT parties block: fourteen fields that mean nothing split in half.
        sections.Item().PreventPageBreak().Element(ComposeParties);
        sections.Item().Element(c => Section(c, "Items Supplied", ComposeItems, paginates: true));
        // Kept whole: a totals block that breaks puts Subtotal on one page and TOTAL on the next, which is
        // the single worst place in the document to make the reader turn over.
        sections.Item().PreventPageBreak().Element(ComposeTotals);

        if (_m.Bank is not null)
        {
            sections.Item().Element(c => Section(c, "Payment Details", ComposeBank));
        }

        sections.Item().PaddingTop(8).Element(ComposeReceipt);
    }

    /// <summary>
    /// The two parties, side by side — supplier on the left, purchaser on the right, in the order the
    /// legacy document used so a reader who knows the old one can find everything in the same place.
    /// </summary>
    /// <remarks>
    /// <b>One table, not two columns of rows.</b> The obvious build is a stack per side, but the two
    /// stacks then advance independently: Smart Net's address wraps to two lines, and every row below it
    /// on the left sits a line lower than its neighbour on the right, so "Telephone No" faces "Contact
    /// Person". A table gives both sides the same row heights, so a long address pushes both down
    /// together and the pairs stay level whatever the data does.
    /// </remarks>
    private void ComposeParties(IContainer container)
    {
        var rows = new (string LeftLabel, string LeftValue, string RightLabel, string RightValue)[]
        {
            ("Date", _m.Date, "Invoice No", _m.InvoiceNo),
            ("Supplier's TIN", _m.Supplier.Tin, "Purchaser's TIN", _m.Purchaser.Tin),
            ("Supplier's Name", _m.Supplier.Name, "Purchaser's Name", _m.Purchaser.Name),
            ("Address", _m.Supplier.Address, "Address", _m.Purchaser.Address),
            ("Telephone No", _m.Supplier.Telephone, "Telephone No", _m.Purchaser.Telephone),
            ("Date of Supply", _m.DateOfSupply, "Contact Person", _m.ContactPerson),
            ("Prepared By", _m.PreparedBy, "PO No", _m.PoNumber),
        };

        container.Table(table =>
        {
            table.ColumnsDefinition(cols =>
            {
                cols.ConstantColumn(88);   // supplier label
                cols.RelativeColumn();     // supplier value
                cols.ConstantColumn(20);   // gutter
                cols.ConstantColumn(88);   // purchaser label
                cols.RelativeColumn();     // purchaser value
            });

            foreach (var (leftLabel, leftValue, rightLabel, rightValue) in rows)
            {
                Label(table.Cell(), leftLabel);
                Value(table.Cell(), leftValue);
                table.Cell().Text(string.Empty);
                Label(table.Cell(), rightLabel);
                Value(table.Cell(), rightValue);
            }
        });
    }

    /// <summary>A party-block label, with the colon attached so every colon on the page lines up.</summary>
    private static void Label(IContainer cell, string text) =>
        cell.PaddingBottom(3).PaddingRight(6).Text($"{text}  :").FontSize(8).Bold().FontColor(Muted);

    private static void Value(IContainer cell, string text) =>
        cell.PaddingBottom(3).Text(Clean(text, "—")).FontSize(9).FontColor(Ink);

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

    /// <summary>
    /// The money column. The legacy report repeated the whole totals box on every page, so a two-page
    /// invoice showed its balance twice; here it appears once, after the last line.
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

                TotalRow(totals, Clean(_m.TaxLabel, "VAT"), _m.TaxAmount);

                totals.Item().PaddingTop(4).LineHorizontal(0.75f).LineColor(Accent);
                totals.Item().PaddingTop(4).Row(r =>
                {
                    r.RelativeItem().Text("TOTAL").FontSize(10).Bold().FontColor(Accent);
                    r.ConstantItem(110).AlignRight().Text(Money(_m.Total)).FontSize(12).Bold().FontColor(Accent);
                });

                // Only when money has actually been received. On an unpaid invoice a "Balance Due" row
                // would just restate the total immediately beneath it.
                if (_m.Paid > 0m)
                {
                    totals.Item().PaddingTop(6);
                    TotalRow(totals, "Paid", _m.Paid);
                    totals.Item().PaddingTop(2).Row(r =>
                    {
                        r.RelativeItem().Text("Balance Due").FontSize(9).Bold().FontColor(Ink);
                        r.ConstantItem(110).AlignRight().Text(Money(_m.BalanceDue)).FontSize(10).Bold().FontColor(Ink);
                    });
                }
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

    private void ComposeReceipt(IContainer container)
    {
        Section(container, "Receipt of Goods", body => body.Column(col =>
        {
            col.Item().Row(row =>
            {
                SignLine(row.RelativeItem(), "Goods Received By");
                row.ConstantItem(20);
                SignLine(row.RelativeItem(), "NIC");
                row.ConstantItem(20);
                SignLine(row.RelativeItem(), $"For {_m.CompanyName}");
            });

            if (_m.BalanceDue > 0m)
            {
                col.Item().PaddingTop(14).Text($"Amount due: {Money(_m.BalanceDue)}.")
                    .FontSize(8.5f).Bold().FontColor(Accent);
            }

            col.Item().PaddingTop(3).Text(
                "Goods remain the property of the company until paid for in full. "
                + "Please quote the invoice number with any payment.")
                .FontSize(7.5f).FontColor(Muted).Italic();
        }));
    }

    private static string Join(params string?[] parts) =>
        string.Join(" — ", parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()));
}
