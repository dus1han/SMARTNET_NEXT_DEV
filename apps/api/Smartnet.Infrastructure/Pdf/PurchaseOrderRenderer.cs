using System.Globalization;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using Smartnet.Domain.Reporting;
using Smartnet.Domain.Settings;
using Smartnet.Infrastructure.Persistence;
using Smartnet.Infrastructure.Reporting;

namespace Smartnet.Infrastructure.Pdf;

/// <summary>
/// Resolves a purchase order, its supplier and its company, and renders the document.
/// </summary>
/// <remarks>
/// <b>Read from the legacy columns</b>, for the reason the quotation renderer gives: <c>PurchaseOrder</c>
/// and <c>PoH</c> map the same <c>po_h</c> row, the typed columns are only populated once an order is
/// adopted, and none of the 124 orders in the dev database has been. The varchars are written by every
/// save, so reading them serves an adopted order and an untouched one alike.
/// </remarks>
public sealed class PurchaseOrderRenderer : IPurchaseOrderRenderer
{
    static PurchaseOrderRenderer() => QuestPDF.Settings.License = LicenseType.Community;

    private readonly SmartnetDbContext _db;
    private readonly SmartnetLegacyDbContext _legacy;

    public PurchaseOrderRenderer(SmartnetDbContext db, SmartnetLegacyDbContext legacy)
    {
        _db = db;
        _legacy = legacy;
    }

    public async Task<byte[]?> RenderAsync(long orderId, CancellationToken cancellationToken = default) =>
        (await BuildAsync(orderId, cancellationToken).ConfigureAwait(false))?.GeneratePdf();

    /// <summary>The composed document rather than its bytes — what the drafting preview tool streams.</summary>
    public async Task<PurchaseOrderDocument?> BuildAsync(long orderId, CancellationToken cancellationToken = default)
    {
        var header = await _legacy.PoHs
            .FirstOrDefaultAsync(p => p.Id == orderId, cancellationToken)
            .ConfigureAwait(false);

        if (header is null)
        {
            return null;
        }

        var number = Trim(header.PoNo);

        // Lines by the real key once adopted, by number until then — po_l carries no order id before
        // adoption. The same rule the quotation renderer follows, and for the same reason.
        var adoptedLines = await _db.PurchaseOrderLines
            .IgnoreQueryFilters()
            .Where(l => l.PurchaseOrderId == orderId && l.DeletedAt == null)
            .OrderBy(l => l.Id)
            .Select(l => new LineValues(l.ItemCode, l.Description, l.Quantity, l.UnitPrice, l.Net, null))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var lines = adoptedLines.Count > 0
            ? adoptedLines
            : await _legacy.PoLs
                .Where(l => l.Pono == number)
                .Select(l => new LineValues(
                    null,
                    l.Desc,
                    LegacyValue.Money(l.Qty),
                    LegacyValue.Money(l.Rate),
                    LegacyValue.Money(l.Total),
                    l.Itemno))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

        long.TryParse(header.Company, NumberStyles.Integer, CultureInfo.InvariantCulture, out var companyId);

        var company = await _db.Companies
            .FirstOrDefaultAsync(c => c.Id == companyId, cancellationToken)
            .ConfigureAwait(false);

        var logo = await _db.CompanyLogos
            .Where(l => l.CompanyId == companyId)
            .Select(l => l.Data)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        // The legacy `supplier` column holds the supplier *code*, not a key.
        var supplierCode = Trim(header.Supplier);
        var supplier = supplierCode.Length == 0
            ? null
            : await _db.Suppliers
                .Where(s => s.Code == supplierCode)
                .Select(s => new { s.Name, s.Address, s.ContactPerson, s.Phone })
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

        var items = lines
            .OrderBy(l => int.TryParse(l.Itemno, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : int.MaxValue)
            .Select(l => new PurchaseOrderItem(Trim(l.ItemCode), Trim(l.Description), l.Quantity, l.Rate, l.Total))
            .ToList();

        var netTotal = LegacyValue.Money(header.Nonvattotal);
        var total = LegacyValue.Money(header.Totamount);
        var vatRate = LegacyValue.Money(header.Vatpercent);

        // po_h has no pre-discount column, so the subtotal is the taxable figure. `vatty` is the legacy
        // "is this order VAT-bearing" flag; the company's own registration still has to agree.
        var vatBearing = Trim(header.Vatty) == "1";
        var vatRegistered = (company?.IsVatRegistered ?? false) && vatBearing && vatRate > 0m;

        var model = new PurchaseOrderModel(
            Logo: logo is { Length: > 0 } ? logo : null,
            CompanyName: Trim(company?.Name),
            CompanyContact: CompanyHeader.Build(company),
            AccentColour: CompanyTheme.AccentFor(companyId),
            OrderNo: number,
            Date: FormatDate(header.Podate),
            SupplierName: Trim(supplier?.Name),
            SupplierAddress: Trim(supplier?.Address),
            SupplierContact: Trim(supplier?.ContactPerson),
            // Grouped by the same formatter the company header uses, so a number reads the same wherever
            // it appears on the document.
            SupplierPhone: string.IsNullOrWhiteSpace(supplier?.Phone)
                ? string.Empty
                : CompanyHeader.FormatPhone(supplier.Phone.Trim()),
            PreparedBy: Trim(header.Preparedby),
            Items: items,
            Subtotal: netTotal,
            DiscountPercent: 0m,
            DiscountAmount: 0m,
            NetTotal: netTotal,
            TaxLabel: vatRegistered ? $"VAT ({Percentage(vatRate)}%)" : null,
            TaxAmount: vatRegistered ? total - netTotal : null,
            Total: total,
            DeliverTo: DeliveryAddress(company));

        return new PurchaseOrderDocument(model);
    }

    /// <summary>Where the goods go — the ordering company's own address, on one line.</summary>
    private static string DeliveryAddress(Company? c)
    {
        if (c is null)
        {
            return string.Empty;
        }

        var parts = new[] { c.AddressLine1, c.AddressLine2, c.City, c.Country }
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!.Trim());

        return string.Join(", ", parts);
    }

    private static string FormatDate(string? raw) =>
        DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.ToString("dd MMM yyyy", CultureInfo.InvariantCulture)
            : Trim(raw);

    private static string Percentage(decimal value) =>
        value == decimal.Truncate(value)
            ? decimal.Truncate(value).ToString("0", CultureInfo.InvariantCulture)
            : value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Trim(string? s) => s?.Trim() ?? string.Empty;

    /// <summary>A line from either source, reduced to the figures the document prints.</summary>
    private sealed record LineValues(
        string? ItemCode,
        string? Description,
        decimal Quantity,
        decimal Rate,
        decimal Total,
        string? Itemno);
}
