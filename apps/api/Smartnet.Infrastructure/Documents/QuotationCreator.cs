using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Smartnet.Domain.Auditing;
using Smartnet.Domain.Documents;
using Smartnet.Domain.MasterData;
using Smartnet.Domain.Settings;
using Smartnet.Infrastructure.Persistence;
using Smartnet.Infrastructure.Persistence.Configurations;

namespace Smartnet.Infrastructure.Documents;

/// <inheritdoc cref="IQuotationCreator"/>
public sealed class QuotationCreator : IQuotationCreator
{
    private readonly SmartnetDbContext _db;
    private readonly ITaxEngine _tax;
    private readonly IDocumentNumberAllocator _numbers;
    private readonly IDocumentVersionWriter _versions;
    private readonly IBusinessRuleReader _rules;
    private readonly IChangeContext _change;
    private readonly TimeProvider _time;

    public QuotationCreator(
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

    public async Task<QuotationCreated> CreateAsync(NewQuotation request, CancellationToken cancellationToken = default)
    {
        var company = await _db.Companies
            .FirstOrDefaultAsync(c => c.Id == request.CompanyId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Company {request.CompanyId} does not exist.");

        var customer = await _db.Customers
            .FirstOrDefaultAsync(c => c.Id == request.CustomerId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Customer {request.CustomerId} does not exist.");

        var rates = await _db.TaxRates
            .Where(r => r.CompanyId == request.CompanyId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var rounding = BusinessRules.RoundPerDocument.Equals(
            await _rules.ResolveAsync(request.CompanyId, BusinessRules.VatRoundingMode, cancellationToken).ConfigureAwait(false),
            StringComparison.Ordinal)
            ? TaxRounding.PerDocument
            : TaxRounding.PerLine;

        // Value the lines: the same one tax engine, resolving the company's rate at the quotation's date
        // and snapshotting it, so a reprint reproduces the figures it was quoted at.
        var calc = _tax.Calculate(new TaxCalculationRequest(
            request.Date,
            company.IsVatRegistered,
            rounding,
            [.. request.Lines.Select(l => new TaxLineInput(l.Quantity, l.UnitPrice, l.DiscountPercent))],
            rates,
            request.DocumentDiscountPercent));

        // One transaction: the number, the header, the lines and the snapshot commit together or not at
        // all. No ledger charge and no stock issue — a quotation is an offer, not a sale. The number is
        // reserved under a row lock inside the transaction, so a failed save rolls it back (B4).
        await using var transaction = await _db.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        var number = await _numbers
            .AllocateAsync(request.CompanyId, DocumentTypes.Quotation, request.Date, cancellationToken)
            .ConfigureAwait(false);

        var lineCost = request.Lines.Sum(l => l.Cost ?? 0m);
        var preparedByName = await PreparedByNameAsync(cancellationToken).ConfigureAwait(false);

        var quotation = new Quotation
        {
            Number = number,
            CompanyId = request.CompanyId,
            CustomerId = request.CustomerId,
            Date = request.Date,
            // contactperson is NOT NULL in the legacy table; an absent contact is an empty string.
            ContactPerson = request.ContactPerson ?? string.Empty,
            Validity = request.Validity ?? string.Empty,
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

            Lines = [.. request.Lines.Zip(calc.Lines, (input, line) => new QuotationLine
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

        _db.Quotations.Add(quotation);
        SetLegacyShadow(quotation, customer, company, preparedByName);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false); // header + lines + audit; assigns ids

        await _versions
            .WriteAsync(DocumentTypes.Quotation, quotation.Id, request.CompanyId, Snapshot(quotation, calc, customer, company), reason: null, cancellationToken)
            .ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return new QuotationCreated(quotation.Id, number, calc.Totals.Total);
    }

    /// <summary>
    /// Writes the legacy varchar columns beside the typed ones, so the surviving legacy reader (a
    /// customer's quote history) sees a complete row. The NOT NULL columns are among them, so the insert
    /// fails without it.
    /// </summary>
    private void SetLegacyShadow(Quotation quotation, Customer customer, Company company, string? preparedByName)
    {
        var entry = _db.Entry(quotation);
        void Set(string name, string? value) => entry.Property(name).CurrentValue = value;

        var hasItem = quotation.Lines.Any(l => l.ItemId is not null);

        Set(QuotationLegacyShadow.It, hasItem ? "ITEM" : "SERVICE");
        Set(QuotationLegacyShadow.QDate, quotation.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Set(QuotationLegacyShadow.Customer, customer.Code);
        Set(QuotationLegacyShadow.TotAmount, Money(quotation.Total));
        Set(QuotationLegacyShadow.PreparedBy, preparedByName);
        Set(QuotationLegacyShadow.CDateTime, _time.GetUtcNow().UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        Set(QuotationLegacyShadow.QuoteCost, Money(quotation.Cost));
        Set(QuotationLegacyShadow.NoVatTotal, Money(quotation.NetTotal));
        Set(QuotationLegacyShadow.VType, company.VatCode);
        Set(QuotationLegacyShadow.VPer, Money(quotation.TaxRatePercentage));
        Set(QuotationLegacyShadow.DiscountPer, Money(quotation.DiscountPercent));
        Set(QuotationLegacyShadow.BeforeDiscTot, Money(quotation.Subtotal));
        Set(QuotationLegacyShadow.Company, quotation.CompanyId?.ToString(CultureInfo.InvariantCulture));

        foreach (var line in quotation.Lines)
        {
            var lineEntry = _db.Entry(line);
            lineEntry.Property(QuotationLineLegacyShadow.Qno).CurrentValue = quotation.Number;
            lineEntry.Property(QuotationLineLegacyShadow.Qty).CurrentValue = Money(line.Quantity);
            lineEntry.Property(QuotationLineLegacyShadow.Rate).CurrentValue = Money(line.UnitPrice);
            lineEntry.Property(QuotationLineLegacyShadow.Total).CurrentValue = Money(line.Gross);
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
    /// A self-contained snapshot — the quotation as issued, resolved not referenced, so a reprint
    /// reproduces it rather than re-resolving today's rate or today's company header.
    /// </summary>
    private static object Snapshot(Quotation quotation, TaxCalculationResult calc, Customer customer, Company company) => new
    {
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
        lines = quotation.Lines.Select(l => new
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
