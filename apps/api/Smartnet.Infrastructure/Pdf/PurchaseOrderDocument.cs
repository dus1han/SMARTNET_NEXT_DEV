using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Smartnet.Infrastructure.Pdf;

/// <summary>
/// The purchase order, in the house layout (see <see cref="HouseDocument"/>) — the same document
/// language as the quotation and the job sheet, carrying an order instead of an offer.
/// </summary>
/// <remarks>
/// One template for every company: the VAT rows appear when the ordering company is registered, which
/// is the only thing the legacy <c>PO_SN</c> / <c>PO_ST</c> pair actually differed on.
/// </remarks>
public sealed class PurchaseOrderDocument : HouseDocument
{
    private readonly PurchaseOrderModel _m;

    public PurchaseOrderDocument(PurchaseOrderModel model) : base(model.AccentColour) => _m = model;

    protected override string Title => "PURCHASE ORDER";

    /// <summary>"Purchase Order" would run past the footer's left half at this size.</summary>
    protected override string FooterName => "Purchase Order";

    protected override byte[]? Logo => _m.Logo;

    protected override string CompanyName => _m.CompanyName;

    protected override string CompanyContact => _m.CompanyContact;

    protected override IReadOnlyList<(string Label, string Value)> References =>
    [
        // "Order No", not "Purchase Order No": the reference label column is shared with every other
        // document, and the longer wording wraps in it. The title already says PURCHASE ORDER.
        ("Order No", _m.OrderNo),
        ("Date", _m.Date),
    ];

    protected override void ComposeSections(ColumnDescriptor sections)
    {
        sections.Item().Element(c => Section(c, "Supplier", body => body.Row(row =>
        {
            row.RelativeItem().Column(left =>
            {
                left.Spacing(6);
                Field(left.Item(), "Supplier", _m.SupplierName);
                Field(left.Item(), "Address", _m.SupplierAddress);
            });
            row.ConstantItem(24);
            row.RelativeItem().Column(right =>
            {
                right.Spacing(6);
                Field(right.Item(), "Contact Person", WithPhone(_m.SupplierContact, _m.SupplierPhone));
                Field(right.Item(), "Prepared By", _m.PreparedBy);
            });
        })));

        sections.Item().Element(c => Section(c, "Items Ordered", ComposeItems));
        sections.Item().Element(ComposeTotals);

        if (Clean(_m.DeliverTo).Length > 0)
        {
            sections.Item().Element(c => Section(c, "Delivery", body =>
                Field(body, "Deliver To", _m.DeliverTo)));
        }

        sections.Item().PaddingTop(8).Element(ComposeAuthorisation);
    }

    private void ComposeItems(IContainer container)
    {
        container.Table(table =>
        {
            // No item-code column: it is an internal reference, and this document is read by the
            // supplier. The space it took goes to the description.
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
                    r.RelativeItem().Text("TOTAL").FontSize(10).Bold().FontColor(Accent);
                    r.ConstantItem(110).AlignRight().Text(Money(_m.Total)).FontSize(12).Bold().FontColor(Accent);
                });
            });
        });
    }

    /// <summary>
    /// Who authorised the order. Two lines rather than the quotation's three — an order is signed by the
    /// buyer, not agreed between two parties, so there is nobody to countersign.
    /// </summary>
    private void ComposeAuthorisation(IContainer container)
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

            col.Item().PaddingTop(14).Text("Please quote this order number on your invoice and delivery note.")
                .FontSize(8.5f).Bold().FontColor(Accent);

            col.Item().PaddingTop(3).Text(
                "Goods remain subject to inspection on delivery. Prices and quantities are as ordered above; "
                + "any variation must be agreed in writing before despatch.")
                .FontSize(7.5f).FontColor(Muted).Italic();
        }));
    }
}
