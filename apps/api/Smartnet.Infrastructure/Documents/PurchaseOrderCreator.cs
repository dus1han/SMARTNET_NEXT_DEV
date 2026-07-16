using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Smartnet.Domain.Auditing;
using Smartnet.Domain.Documents;
using Smartnet.Domain.MasterData;
using Smartnet.Domain.Settings;
using Smartnet.Infrastructure.Persistence;
using Smartnet.Infrastructure.Persistence.Configurations;

namespace Smartnet.Infrastructure.Documents;

/// <inheritdoc cref="IPurchaseOrderCreator"/>
public sealed class PurchaseOrderCreator : IPurchaseOrderCreator
{
    private readonly SmartnetDbContext _db;
    private readonly ITaxEngine _tax;
    private readonly IDocumentNumberAllocator _numbers;
    private readonly IDocumentVersionWriter _versions;
    private readonly IBusinessRuleReader _rules;
    private readonly IChangeContext _change;
    private readonly TimeProvider _time;

    public PurchaseOrderCreator(
        SmartnetDbContext db,
        ITaxEngine tax,
        IDocumentNumberAllocator numbers,
        IDocumentVersionWriter versions,
        IBusinessRuleReader rules,
        IChangeContext change,
        TimeProvider time)
    {
        _db = db;
        _tax = tax;
        _numbers = numbers;
        _versions = versions;
        _rules = rules;
        _change = change;
        _time = time;
    }

    public async Task<PurchaseOrderCreated> CreateAsync(NewPurchaseOrder request, CancellationToken cancellationToken = default)
    {
        var company = await _db.Companies
            .FirstOrDefaultAsync(c => c.Id == request.CompanyId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Company {request.CompanyId} does not exist.");

        var supplier = await _db.Suppliers
            .FirstOrDefaultAsync(s => s.Id == request.SupplierId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Supplier {request.SupplierId} does not exist.");

        var rates = await _db.TaxRates
            .Where(r => r.CompanyId == request.CompanyId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var rounding = BusinessRules.RoundPerDocument.Equals(
            await _rules.ResolveAsync(request.CompanyId, BusinessRules.VatRoundingMode, cancellationToken).ConfigureAwait(false),
            StringComparison.Ordinal)
            ? TaxRounding.PerDocument
            : TaxRounding.PerLine;

        // Value the lines: the same one tax engine, resolving the company's rate at the PO's date and
        // snapshotting it, so a reprint reproduces the figures it was ordered at (the legacy CURDATE bug).
        var calc = _tax.Calculate(new TaxCalculationRequest(
            request.Date,
            company.IsVatRegistered,
            rounding,
            [.. request.Lines.Select(l => new TaxLineInput(l.Quantity, l.UnitPrice, l.DiscountPercent))],
            rates,
            request.DocumentDiscountPercent));

        // One transaction: the number, the header, the lines and the snapshot commit together or not at
        // all. No ledger charge and no stock receipt — a PO is an order (the payable is the supplier
        // invoice; the receipt is the deferred GRN). The number is reserved under a row lock inside the
        // transaction, so a failed save rolls it back (B4).
        await using var transaction = await _db.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        var number = await _numbers
            .AllocateAsync(request.CompanyId, DocumentTypes.PurchaseOrder, request.Date, cancellationToken)
            .ConfigureAwait(false);

        var lineCost = request.Lines.Sum(l => l.Cost ?? 0m);
        var preparedByName = await PreparedByNameAsync(cancellationToken).ConfigureAwait(false);

        var order = new PurchaseOrder
        {
            Number = number,
            CompanyId = request.CompanyId,
            SupplierId = request.SupplierId,
            Date = request.Date,
            PreparedBy = _change.UserId,

            Subtotal = calc.Totals.Subtotal,
            DiscountPercent = request.DocumentDiscountPercent,
            DiscountAmount = calc.Totals.Discount,
            NetTotal = calc.Totals.Net,
            TaxRateId = calc.TaxRateId,
            TaxRatePercentage = calc.TaxRatePercentage,
            TaxAmount = calc.Totals.Tax,
            Total = calc.Totals.Total,
            Cost = lineCost,
            DataOrigin = "new",

            Lines = [.. request.Lines.Zip(calc.Lines, (input, line) => new PurchaseOrderLine
            {
                ItemId = input.ItemId,
                ItemCode = input.ItemCode,
                Description = input.Description,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                DiscountPercent = line.DiscountPercent,
                Gross = line.Gross,
                Net = line.Net,
                Cost = input.Cost,
            })],
        };

        _db.PurchaseOrders.Add(order);
        SetLegacyShadow(order, supplier, company, preparedByName);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false); // header + lines + audit; assigns ids

        await _versions
            .WriteAsync(DocumentTypes.PurchaseOrder, order.Id, request.CompanyId, Snapshot(order, calc, supplier, company), reason: null, cancellationToken)
            .ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return new PurchaseOrderCreated(order.Id, number, calc.Totals.Total);
    }

    /// <summary>
    /// Writes the legacy varchar columns beside the typed ones, so the surviving legacy readers (SearchPO's
    /// list, the PO reprint) see a complete row. Unlike invoices/quotations, no po_h column is NOT NULL, so
    /// this keeps the reader whole rather than gating the insert.
    /// </summary>
    private void SetLegacyShadow(PurchaseOrder order, Supplier supplier, Company company, string? preparedByName)
    {
        var entry = _db.Entry(order);
        void Set(string name, string? value) => entry.Property(name).CurrentValue = value;

        Set(PurchaseOrderLegacyShadow.PoDate, order.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Set(PurchaseOrderLegacyShadow.Supplier, supplier.Code);
        Set(PurchaseOrderLegacyShadow.TotAmount, Money(order.Total));
        Set(PurchaseOrderLegacyShadow.PreparedBy, preparedByName);
        Set(PurchaseOrderLegacyShadow.CDateTime, _time.GetUtcNow().UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        Set(PurchaseOrderLegacyShadow.NonVatTotal, Money(order.NetTotal));
        Set(PurchaseOrderLegacyShadow.VatTy, company.VatCode);
        Set(PurchaseOrderLegacyShadow.VatPercent, Money(order.TaxRatePercentage));
        Set(PurchaseOrderLegacyShadow.Company, order.CompanyId?.ToString(CultureInfo.InvariantCulture));

        foreach (var line in order.Lines)
        {
            var lineEntry = _db.Entry(line);
            lineEntry.Property(PurchaseOrderLineLegacyShadow.Pono).CurrentValue = order.Number;
            lineEntry.Property(PurchaseOrderLineLegacyShadow.Qty).CurrentValue = Money(line.Quantity);
            lineEntry.Property(PurchaseOrderLineLegacyShadow.Rate).CurrentValue = Money(line.UnitPrice);
            lineEntry.Property(PurchaseOrderLineLegacyShadow.Total).CurrentValue = Money(line.Gross);
        }
    }

    /// <summary>The signed-in user's name, for the legacy <c>preparedby</c> — null if none is set.</summary>
    private async Task<string?> PreparedByNameAsync(CancellationToken cancellationToken)
    {
        if (_change.UserId is not { } userId)
        {
            return null;
        }

        return await _db.Users
            .Where(u => u.Id == userId)
            .Select(u => u.Name ?? u.Username)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static string Money(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// A self-contained snapshot — the PO as issued, resolved not referenced, so a reprint reproduces it
    /// rather than re-resolving today's rate or today's company header.
    /// </summary>
    private static object Snapshot(PurchaseOrder order, TaxCalculationResult calc, Supplier supplier, Company company) => new
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
        supplier = new { supplier.Id, supplier.Code, supplier.Name, supplier.VatNumber },
        company = new
        {
            company.Id,
            company.Name,
            company.VatNumber,
            company.AddressLine1,
            company.AddressLine2,
            company.City,
            company.Country,
        },
        lines = order.Lines.Select(l => new
        {
            l.ItemId,
            l.ItemCode,
            l.Description,
            l.Quantity,
            l.UnitPrice,
            l.DiscountPercent,
            l.Gross,
            l.Net,
            l.Cost,
        }),
    };
}
