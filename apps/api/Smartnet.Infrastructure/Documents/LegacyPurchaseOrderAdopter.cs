using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Smartnet.Domain.Auditing;
using Smartnet.Domain.Documents;
using Smartnet.Domain.MasterData;
using Smartnet.Domain.Settings;
using Smartnet.Infrastructure.Persistence;
using Smartnet.Infrastructure.Persistence.Configurations;
using Smartnet.Infrastructure.Reporting;

namespace Smartnet.Infrastructure.Documents;

/// <inheritdoc cref="ILegacyPurchaseOrderAdopter"/>
public sealed class LegacyPurchaseOrderAdopter : ILegacyPurchaseOrderAdopter
{
    private readonly SmartnetDbContext _db;
    private readonly ITaxEngine _tax;
    private readonly IDocumentVersionWriter _versions;
    private readonly IBusinessRuleReader _rules;

    public LegacyPurchaseOrderAdopter(
        SmartnetDbContext db,
        ITaxEngine tax,
        IDocumentVersionWriter versions,
        IBusinessRuleReader rules)
    {
        _db = db;
        _tax = tax;
        _versions = versions;
        _rules = rules;
    }

    public async Task MaterialiseInCurrentTransactionAsync(
        PurchaseOrder order,
        CancellationToken cancellationToken = default)
    {
        if (string.Equals(order.DataOrigin, "new", StringComparison.Ordinal))
        {
            return;
        }

        var entry = _db.Entry(order);
        string? Shadow(string name) => entry.Property(name).CurrentValue as string;

        if (order.CompanyId is not { } companyId)
        {
            throw new LegacyDocumentNotAdoptableException(order.Number, "it has no company.");
        }

        var company = await _db.Companies
            .FirstOrDefaultAsync(c => c.Id == companyId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new LegacyDocumentNotAdoptableException(order.Number, "its company no longer exists.");

        // The legacy `supplier` column holds the supplier code, not a key.
        var supplierCode = Shadow(PurchaseOrderLegacyShadow.Supplier);
        var supplier = supplierCode is null
            ? null
            : await _db.Suppliers.FirstOrDefaultAsync(s => s.Code == supplierCode, cancellationToken).ConfigureAwait(false);

        if (supplier is null)
        {
            throw new LegacyDocumentNotAdoptableException(order.Number, $"its supplier '{supplierCode}' is not in the supplier master.");
        }

        var date = LegacyValue.Date(Shadow(PurchaseOrderLegacyShadow.PoDate))
            ?? throw new LegacyDocumentNotAdoptableException(order.Number, "its date could not be read.");

        var vatPercent = LegacyValue.Money(Shadow(PurchaseOrderLegacyShadow.VatPercent));

        // Lines are linked by `pono` until adoption — there is no purchase_order_id on them yet.
        var lineEntities = await _db.PurchaseOrderLines
            .IgnoreQueryFilters()
            .Where(l => EF.Property<string>(l, PurchaseOrderLineLegacyShadow.Pono) == order.Number && l.DeletedAt == null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (lineEntities.Count == 0)
        {
            throw new LegacyDocumentNotAdoptableException(order.Number, "it has no lines to value.");
        }

        var codes = lineEntities
            .Select(l => l.ItemCode)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var items = (await _db.Items
            .Where(i => i.Code != null && codes.Contains(i.Code))
            .Select(i => new { i.Id, i.Code, i.Cost })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false))
            .ToDictionary(i => i.Code!, i => (i.Id, i.Cost), StringComparer.Ordinal);

        var lineInputs = lineEntities
            .Select(l => (
                Entity: l,
                Quantity: LegacyValue.Money(_db.Entry(l).Property(PurchaseOrderLineLegacyShadow.Qty).CurrentValue as string),
                Rate: LegacyValue.Money(_db.Entry(l).Property(PurchaseOrderLineLegacyShadow.Rate).CurrentValue as string)))
            .ToList();

        var rounding = BusinessRules.RoundPerDocument.Equals(
            await _rules.ResolveAsync(companyId, BusinessRules.VatRoundingMode, cancellationToken).ConfigureAwait(false),
            StringComparison.Ordinal)
            ? TaxRounding.PerDocument
            : TaxRounding.PerLine;

        // Valued at the rate the order was raised under, not today's — an adoption records what the
        // document already was, it does not re-rate it.
        var rateName = vatPercent == 0m
            ? "No VAT"
            : $"VAT {vatPercent.ToString("0.##", CultureInfo.InvariantCulture)}%";

        var calc = _tax.Calculate(new TaxCalculationRequest(
            date, company.IsVatRegistered, rounding,
            [.. lineInputs.Select(l => new TaxLineInput(l.Quantity, l.Rate, 0m))],
            AvailableRates: [], DocumentDiscountPercent: 0m,
            RateOverride: new TaxRateOverride(null, rateName, vatPercent)));

        order.SupplierId = supplier.Id;
        order.Date = date;
        order.DiscountPercent = 0m;             // po_h carries no discount column
        order.DiscountAmount = calc.Totals.Discount;
        order.Subtotal = calc.Totals.Subtotal;
        order.NetTotal = calc.Totals.Net;
        order.TaxRateId = null;
        order.TaxRatePercentage = vatPercent;
        order.TaxAmount = calc.Totals.Tax;
        order.Total = calc.Totals.Total;
        order.DataOrigin = "new";

        decimal totalCost = 0m;

        foreach (var ((entity, _, _), line) in lineInputs.Zip(calc.Lines))
        {
            var matched = entity.ItemCode is not null && items.ContainsKey(entity.ItemCode);
            var cost = matched ? items[entity.ItemCode!].Cost : (decimal?)null;

            entity.PurchaseOrderId = order.Id;
            entity.ItemId = matched ? items[entity.ItemCode!].Id : null;
            entity.Quantity = line.Quantity;
            entity.UnitPrice = line.UnitPrice;
            entity.DiscountPercent = 0m;
            entity.Gross = line.Gross;
            entity.Net = line.Net;
            entity.Cost = cost;
            totalCost += cost ?? 0m;

            if (!order.Lines.Contains(entity))
            {
                order.Lines.Add(entity);
            }
        }

        order.Cost = totalCost;

        // Version 1 — the order as it arrived. The edit that follows becomes version 2, so the History tab
        // can show what actually changed rather than presenting the edit as the document's whole life.
        await _versions
            .WriteAsync(DocumentTypes.PurchaseOrder, order.Id, companyId, Snapshot(order, calc, supplier, company),
                reason: "Adopted from the legacy system", cancellationToken)
            .ConfigureAwait(false);
    }

    private static object Snapshot(PurchaseOrder order, TaxCalculationResult calc, Supplier supplier, Company company) => new
    {
        adopted = true,
        purchaseOrder = new
        {
            order.Number,
            order.Date,
            order.Subtotal,
            order.DiscountAmount,
            order.NetTotal,
            order.TaxAmount,
            order.Total,
            order.Cost,
            tax = new { calc.TaxRateId, calc.TaxRateName, calc.TaxRatePercentage },
        },
        supplier = new { supplier.Id, supplier.Code, supplier.Name, supplier.VatNumber },
        company = new { company.Id, company.Name, company.VatNumber },
        lines = order.Lines.Where(l => l.DeletedAt is null).Select(l => new
        {
            l.ItemId, l.ItemCode, l.Description, l.Quantity, l.UnitPrice, l.Gross, l.Net, l.Cost,
        }),
    };
}
