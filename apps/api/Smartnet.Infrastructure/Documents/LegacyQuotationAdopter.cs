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

/// <inheritdoc cref="ILegacyQuotationAdopter"/>
public sealed class LegacyQuotationAdopter : ILegacyQuotationAdopter
{
    private readonly SmartnetDbContext _db;
    private readonly ITaxEngine _tax;
    private readonly IDocumentVersionWriter _versions;
    private readonly IBusinessRuleReader _rules;

    public LegacyQuotationAdopter(
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

    public async Task<long> AdoptAsync(long quotationId, CancellationToken cancellationToken = default)
    {
        await using var transaction = await _db.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        var quotation = await _db.Quotations
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(q => q.Id == quotationId && q.DeletedAt == null, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Quotation {quotationId} does not exist.");

        await MaterialiseInCurrentTransactionAsync(quotation, cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return quotation.Id;
    }

    public async Task MaterialiseInCurrentTransactionAsync(Quotation quotation, CancellationToken cancellationToken = default)
    {
        if (string.Equals(quotation.DataOrigin, "new", StringComparison.Ordinal))
        {
            return;
        }

        var entry = _db.Entry(quotation);
        string? Shadow(string name) => entry.Property(name).CurrentValue as string;

        if (quotation.CompanyId is not { } companyId)
        {
            throw new LegacyDocumentNotAdoptableException(quotation.Number, "it has no company.");
        }

        var company = await _db.Companies
            .FirstOrDefaultAsync(c => c.Id == companyId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new LegacyDocumentNotAdoptableException(quotation.Number, "its company no longer exists.");

        var customerCode = Shadow(QuotationLegacyShadow.Customer);
        var customer = customerCode is null
            ? null
            : await _db.Customers.FirstOrDefaultAsync(c => c.Code == customerCode, cancellationToken).ConfigureAwait(false);
        if (customer is null)
        {
            throw new LegacyDocumentNotAdoptableException(quotation.Number, $"its customer '{customerCode}' is not in the customer master.");
        }

        var date = LegacyValue.Date(Shadow(QuotationLegacyShadow.QDate))
            ?? throw new LegacyDocumentNotAdoptableException(quotation.Number, "its date could not be read.");

        var vper = LegacyValue.Money(Shadow(QuotationLegacyShadow.VPer));
        var documentDiscount = LegacyValue.Money(Shadow(QuotationLegacyShadow.DiscountPer));

        var lineEntities = await _db.QuotationLines
            .IgnoreQueryFilters()
            .Where(l => EF.Property<string>(l, QuotationLineLegacyShadow.Qno) == quotation.Number && l.DeletedAt == null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (lineEntities.Count == 0)
        {
            throw new LegacyDocumentNotAdoptableException(quotation.Number, "it has no lines to value.");
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
                Quantity: LegacyValue.Money(_db.Entry(l).Property(QuotationLineLegacyShadow.Qty).CurrentValue as string),
                Rate: LegacyValue.Money(_db.Entry(l).Property(QuotationLineLegacyShadow.Rate).CurrentValue as string)))
            .ToList();

        var rounding = BusinessRules.RoundPerDocument.Equals(
            await _rules.ResolveAsync(companyId, BusinessRules.VatRoundingMode, cancellationToken).ConfigureAwait(false),
            StringComparison.Ordinal)
            ? TaxRounding.PerDocument
            : TaxRounding.PerLine;

        var rateName = vper == 0m ? "No VAT" : $"VAT {vper.ToString("0.##", CultureInfo.InvariantCulture)}%";
        var calc = _tax.Calculate(new TaxCalculationRequest(
            date, company.IsVatRegistered, rounding,
            [.. lineInputs.Select(l => new TaxLineInput(l.Quantity, l.Rate, 0m))],
            AvailableRates: [], documentDiscount,
            RateOverride: new TaxRateOverride(null, rateName, vper)));

        quotation.CustomerId = customer.Id;
        quotation.Date = date;
        quotation.DiscountPercent = documentDiscount;
        quotation.DiscountAmount = calc.Totals.Discount;
        quotation.Subtotal = calc.Totals.Subtotal;
        quotation.NetTotal = calc.Totals.Net;
        quotation.TaxRateId = null;
        quotation.TaxRatePercentage = vper;
        quotation.TaxAmount = calc.Totals.Tax;
        quotation.Total = calc.Totals.Total;
        quotation.DataOrigin = "new";

        decimal totalCost = 0m;
        foreach (var ((entity, _, _), line) in lineInputs.Zip(calc.Lines))
        {
            var matched = entity.ItemCode is not null && items.TryGetValue(entity.ItemCode, out var item);
            var cost = matched ? items[entity.ItemCode!].Cost : (decimal?)null;

            entity.QuotationId = quotation.Id;
            entity.ItemId = matched ? items[entity.ItemCode!].Id : null;
            entity.Quantity = line.Quantity;
            entity.UnitPrice = line.UnitPrice;
            entity.DiscountPercent = 0m;
            entity.Gross = line.Gross;
            entity.Net = line.Net;
            entity.Cost = cost;
            totalCost += cost ?? 0m;

            if (!quotation.Lines.Contains(entity))
            {
                quotation.Lines.Add(entity);
            }
        }

        quotation.Cost = totalCost;

        await _versions
            .WriteAsync(DocumentTypes.Quotation, quotation.Id, companyId, Snapshot(quotation, calc, customer, company),
                reason: "Adopted from the legacy system", cancellationToken)
            .ConfigureAwait(false);
    }

    private static object Snapshot(Quotation quotation, TaxCalculationResult calc, Customer customer, Company company) => new
    {
        adopted = true,
        quotation = new
        {
            quotation.Number,
            quotation.Date,
            quotation.Validity,
            quotation.ContactPerson,
            quotation.Subtotal,
            quotation.DiscountAmount,
            quotation.NetTotal,
            quotation.TaxAmount,
            quotation.Total,
            quotation.Cost,
            tax = new { calc.TaxRateId, calc.TaxRateName, calc.TaxRatePercentage },
        },
        customer = new { customer.Id, customer.Code, customer.Name, customer.VatNumber },
        company = new { company.Id, company.Name, company.VatNumber },
        lines = quotation.Lines.Where(l => l.DeletedAt is null).Select(l => new
        {
            l.ItemId, l.ItemCode, l.Description, l.Quantity, l.UnitPrice, l.Gross, l.Net, l.Cost,
        }),
    };
}
