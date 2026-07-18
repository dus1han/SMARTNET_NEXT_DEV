using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Smartnet.Infrastructure.Pdf;

/// <summary>
/// The invoice, in the house layout (see <see cref="HouseDocument"/>) — what differs from the other
/// documents is its content, not its appearance.
/// </summary>
/// <remarks>
/// <b>This is the <c>Invoice_ST</c> replacement only — the non-VAT invoice.</b> The VAT pair
/// (<c>Invoice_SN</c> / <c>Invoice_SN_TAX</c>) is deliberately not built here: a tax invoice has its own
/// legal content — the supplier and purchaser registration block — and printing this layout for a
/// VAT-registered company would produce a document the customer cannot reclaim against. The renderer
/// refuses rather than silently omitting the VAT, so the gap is visible instead of wrong.
///
/// <para>Two things this document has that a quotation does not: the settlement rows (what has been paid
/// and what is still owed), and a receipt block instead of an acceptance block — an invoice is a demand
/// for money, not an offer, so nobody signs to accept it.</para>
/// </remarks>
public sealed class InvoiceDocument : HouseDocument
{
    private readonly InvoiceModel _m;

    public InvoiceDocument(InvoiceModel model) : base(model.AccentColour) => _m = model;

    protected override string Title => "INVOICE";

    protected override byte[]? Logo => _m.Logo;

    protected override string CompanyName => _m.CompanyName;

    protected override string CompanyContact => _m.CompanyContact;

    protected override IReadOnlyList<(string Label, string Value)> References
    {
        get
        {
            var rows = new List<(string, string)>
            {
                ("Invoice No", _m.InvoiceNo),
                ("Date", _m.Date),
            };

            if (Clean(_m.InvoiceType).Length > 0)
            {
                rows.Add(("Terms", Clean(_m.InvoiceType)));
            }

            if (Clean(_m.PoNumber).Length > 0)
            {
                rows.Add(("PO Number", Clean(_m.PoNumber)));
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

        sections.Item().Element(c => Section(c, "Items Invoiced", ComposeItems));
        sections.Item().Element(ComposeTotals);

        if (_m.Bank is not null)
        {
            sections.Item().Element(c => Section(c, "Payment Details", ComposeBank));
        }

        sections.Item().PaddingTop(8).Element(ComposeReceipt);
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
    /// lines they come from. No VAT rows — this is the non-VAT invoice. The settlement rows appear only
    /// once something has been paid.
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

    /// <summary>
    /// The receipt block — the legacy invoice's "Goods Received By · NIC · Authorized Signature".
    /// </summary>
    /// <remarks>
    /// Kept because it is not decoration: the signature and NIC are what the business relies on to show a
    /// delivery was accepted, and the quotation's "Accepted By" wording would be wrong here — an invoice
    /// is not an offer to accept.
    /// </remarks>
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
