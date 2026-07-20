using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Smartnet.Domain.Auditing;
using Smartnet.Domain.Documents;
using Smartnet.Domain.Settings;
using Smartnet.Infrastructure.Persistence;
using Smartnet.Infrastructure.Persistence.Configurations;

namespace Smartnet.Infrastructure.Documents;

/// <summary>
/// Editing and voiding a purchase order — one service behind both interfaces, as the creator and workflow
/// share a scope elsewhere.
/// </summary>
/// <remarks>
/// The quotation's shape without the customer: an order posts <b>no ledger entry and no stock movement</b>,
/// so an edit re-values the document and nothing else, and a void is a soft delete with nothing to reverse.
/// The payable arrives with the supplier invoice; the stock arrives with the goods.
///
/// <para>There is no legacy adopter for purchase orders, so an edit works on the typed columns directly and
/// dual-writes the legacy varchars beside them — the same shadow the creator writes, so the surviving
/// legacy reader keeps seeing a complete row.</para>
/// </remarks>
public sealed class PurchaseOrderService : IPurchaseOrderEditor, IPurchaseOrderDeleter
{
    private readonly SmartnetDbContext _db;
    private readonly ITaxEngine _tax;
    private readonly IDocumentVersionWriter _versions;
    private readonly ILegacyPurchaseOrderAdopter _adopter;
    private readonly IBusinessRuleReader _rules;
    private readonly IChangeContext _change;
    private readonly TimeProvider _time;

    public PurchaseOrderService(
        SmartnetDbContext db,
        ITaxEngine tax,
        IDocumentVersionWriter versions,
        ILegacyPurchaseOrderAdopter adopter,
        IBusinessRuleReader rules,
        IChangeContext change,
        TimeProvider time)
    {
        _db = db;
        _tax = tax;
        _versions = versions;
        _adopter = adopter;
        _rules = rules;
        _change = change;
        _time = time;
    }

    public async Task<PurchaseOrderEdited> EditAsync(
        long orderId,
        EditPurchaseOrder request,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await _db.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        var order = await LoadAsync(orderId, cancellationToken).ConfigureAwait(false);

        // The load's row_version is replaced by the caller's, so the UPDATE checks *their* expectation.
        _db.Entry(order).Property(o => o.RowVersion).OriginalValue = request.ExpectedRowVersion;

        // A legacy order is adopted into the new model before it can be edited — its typed columns and
        // lines are materialised from the legacy data and a version-1 "as imported" snapshot is written,
        // inside this transaction (the concurrency check above fires on that first save). Without it the
        // edit would find no existing lines to reconcile against, since a legacy line is linked by `pono`
        // rather than by key, and every line would be re-created as new. A no-op for an adopted order.
        await _adopter.MaterialiseInCurrentTransactionAsync(order, cancellationToken).ConfigureAwait(false);

        var company = await _db.Companies
            .FirstOrDefaultAsync(c => c.Id == order.CompanyId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Company {order.CompanyId} does not exist.");

        var rounding = BusinessRules.RoundPerDocument.Equals(
            await _rules.ResolveAsync(company.Id, BusinessRules.VatRoundingMode, cancellationToken).ConfigureAwait(false),
            StringComparison.Ordinal)
            ? TaxRounding.PerDocument
            : TaxRounding.PerLine;

        // Moving the date re-rates the order at the rate in force then; leaving it alone keeps the rate it
        // was raised under. Nothing posted, so nothing moves with it.
        TaxCalculationResult calc;

        if (request.Date is { } moved && moved != order.Date)
        {
            var rates = await _db.TaxRates
                .Where(r => r.CompanyId == company.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            calc = _tax.Calculate(new TaxCalculationRequest(
                moved, company.IsVatRegistered, rounding,
                [.. request.Lines.Select(l => new TaxLineInput(l.Quantity, l.UnitPrice, l.DiscountPercent))],
                rates, request.DocumentDiscountPercent));

            order.Date = moved;
            order.TaxRateId = calc.TaxRateId;
            order.TaxRatePercentage = calc.TaxRatePercentage;
        }
        else
        {
            var rateName = order.TaxRatePercentage == 0m
                ? "No VAT"
                : $"VAT {order.TaxRatePercentage.ToString("0.##", CultureInfo.InvariantCulture)}%";

            calc = _tax.Calculate(new TaxCalculationRequest(
                order.Date, company.IsVatRegistered, rounding,
                [.. request.Lines.Select(l => new TaxLineInput(l.Quantity, l.UnitPrice, l.DiscountPercent))],
                AvailableRates: [], request.DocumentDiscountPercent,
                RateOverride: new TaxRateOverride(order.TaxRateId, rateName, order.TaxRatePercentage)));
        }

        ReconcileLines(order, request, calc);

        order.DiscountPercent = request.DocumentDiscountPercent;
        order.DiscountAmount = calc.Totals.Discount;
        order.Subtotal = calc.Totals.Subtotal;
        order.NetTotal = calc.Totals.Net;
        order.TaxAmount = calc.Totals.Tax;
        order.Total = calc.Totals.Total;
        order.Cost = request.DocumentCost ?? DocumentCostBasis.Of(request.Lines.Select(l => (l.Cost, l.Quantity)));

        UpdateLegacyShadow(order);

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var versionNo = await _versions
            .WriteAsync(DocumentTypes.PurchaseOrder, order.Id, order.CompanyId, Snapshot(order, calc), _change.Reason, cancellationToken)
            .ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return new PurchaseOrderEdited(order.Id, order.Number, order.Total, versionNo);
    }

    public async Task<PurchaseOrderDeleted> DeleteAsync(
        long orderId,
        int expectedRowVersion,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await _db.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        var order = await LoadAsync(orderId, cancellationToken).ConfigureAwait(false);

        _db.Entry(order).Property(o => o.RowVersion).OriginalValue = expectedRowVersion;

        // Soft-delete by setting deleted_at directly rather than Remove() — the interceptor's "WasDeleted"
        // path, which writes the audit row carrying the reason and does not cascade. There is nothing to
        // reverse: an order posted no ledger entry and no stock movement.
        var now = _time.GetUtcNow().UtcDateTime;

        foreach (var line in order.Lines.Where(l => l.DeletedAt is null))
        {
            line.DeletedAt = now;
        }

        order.DeletedAt = now;

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return new PurchaseOrderDeleted(order.Id, order.Number);
    }

    private async Task<PurchaseOrder> LoadAsync(long orderId, CancellationToken cancellationToken) =>
        await _db.PurchaseOrders
            .IgnoreQueryFilters()
            .Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == orderId && o.DeletedAt == null, cancellationToken)
            .ConfigureAwait(false)
        ?? throw new InvalidOperationException($"Purchase order {orderId} does not exist.");

    /// <summary>Reconciles the lines in place — update / add / remove. No stock (an order receives none).</summary>
    private void ReconcileLines(PurchaseOrder order, EditPurchaseOrder request, TaxCalculationResult calc)
    {
        var existing = order.Lines.Where(l => l.DeletedAt is null).ToDictionary(l => l.Id);
        var kept = new HashSet<long>();

        foreach (var (input, line) in request.Lines.Zip(calc.Lines))
        {
            if (input.Id is { } id && existing.TryGetValue(id, out var current))
            {
                current.ItemId = input.ItemId;
                current.ItemCode = input.ItemCode;
                current.Description = input.Description;
                current.Quantity = line.Quantity;
                current.UnitPrice = line.UnitPrice;
                current.DiscountPercent = line.DiscountPercent;
                current.Gross = line.Gross;
                current.Net = line.Net;
                current.Cost = input.Cost;
                SetLineShadow(current, order.Number);
                kept.Add(id);
            }
            else
            {
                var added = new PurchaseOrderLine
                {
                    PurchaseOrderId = order.Id,
                    ItemId = input.ItemId,
                    ItemCode = input.ItemCode,
                    Description = input.Description,
                    Quantity = line.Quantity,
                    UnitPrice = line.UnitPrice,
                    DiscountPercent = line.DiscountPercent,
                    Gross = line.Gross,
                    Net = line.Net,
                    Cost = input.Cost,
                };

                order.Lines.Add(added); // via the tracked navigation, so the version snapshot sees it
                SetLineShadow(added, order.Number);
            }
        }

        foreach (var line in existing.Values.Where(l => !kept.Contains(l.Id)))
        {
            _db.PurchaseOrderLines.Remove(line);
        }
    }

    /// <summary>Dual-writes the legacy varchars beside the typed columns, as the creator does.</summary>
    private void UpdateLegacyShadow(PurchaseOrder order)
    {
        var entry = _db.Entry(order);
        void Set(string name, string? value) => entry.Property(name).CurrentValue = value;

        Set(PurchaseOrderLegacyShadow.PoDate, order.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Set(PurchaseOrderLegacyShadow.TotAmount, Money(order.Total));
        Set(PurchaseOrderLegacyShadow.NonVatTotal, Money(order.NetTotal));
        Set(PurchaseOrderLegacyShadow.VatTy, order.TaxRatePercentage > 0m ? "1" : "2");
        Set(PurchaseOrderLegacyShadow.VatPercent, Money(order.TaxRatePercentage));
    }

    private void SetLineShadow(PurchaseOrderLine line, string number)
    {
        var entry = _db.Entry(line);
        entry.Property(PurchaseOrderLineLegacyShadow.Pono).CurrentValue = number;
        entry.Property(PurchaseOrderLineLegacyShadow.Qty).CurrentValue = Money(line.Quantity);
        entry.Property(PurchaseOrderLineLegacyShadow.Rate).CurrentValue = Money(line.UnitPrice);
        entry.Property(PurchaseOrderLineLegacyShadow.Total).CurrentValue = Money(line.Gross);
    }

    private static string Money(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>The new version's snapshot — the order as it now stands, resolved not referenced.</summary>
    private static object Snapshot(PurchaseOrder order, TaxCalculationResult calc) => new
    {
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
        lines = order.Lines.Where(l => l.DeletedAt is null).Select(l => new
        {
            l.ItemId, l.ItemCode, l.Description, l.Quantity, l.UnitPrice, l.DiscountPercent, l.Gross, l.Net, l.Cost,
        }),
    };
}
