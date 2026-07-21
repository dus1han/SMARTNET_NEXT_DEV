using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Smartnet.Domain.Auditing;
using Smartnet.Domain.Documents;
using Smartnet.Domain.Settings;
using Smartnet.Infrastructure.Persistence;
using Smartnet.Infrastructure.Persistence.Configurations;

namespace Smartnet.Infrastructure.Documents;

/// <inheritdoc cref="IQuotationEditor"/>
public sealed class QuotationEditor : IQuotationEditor
{
    private readonly SmartnetDbContext _db;
    private readonly ITaxEngine _tax;
    private readonly IDocumentVersionWriter _versions;
    private readonly ILegacyQuotationAdopter _adopter;
    private readonly IBusinessRuleReader _rules;
    private readonly IChangeContext _change;

    public QuotationEditor(
        SmartnetDbContext db,
        ITaxEngine tax,
        IDocumentVersionWriter versions,
        ILegacyQuotationAdopter adopter,
        IBusinessRuleReader rules,
        IChangeContext change)
    {
        _db = db;
        _tax = tax;
        _versions = versions;
        _adopter = adopter;
        _rules = rules;
        _change = change;
    }

    public async Task<QuotationEdited> EditAsync(long quotationId, EditQuotation request, CancellationToken cancellationToken = default)
    {
        await using var transaction = await _db.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        var quotation = await _db.Quotations
            .IgnoreQueryFilters()
            .Include(q => q.Lines)
            .FirstOrDefaultAsync(q => q.Id == quotationId && q.DeletedAt == null, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Quotation {quotationId} does not exist.");

        // A spent quote is not editable — it became an invoice; changing it would desync the two.
        if (quotation.ConvertedToInvoiceId is { } invoiceId)
        {
            throw new QuotationAlreadyConvertedException(quotation.Number, invoiceId);
        }

        _db.Entry(quotation).Property(q => q.RowVersion).OriginalValue = request.ExpectedRowVersion;

        // A legacy quotation is adopted into the new model first (materialise + version-1), inside this
        // transaction; the concurrency check fires on that first save. A no-op for a new quotation.
        await _adopter.MaterialiseInCurrentTransactionAsync(quotation, cancellationToken).ConfigureAwait(false);

        var company = await _db.Companies
            .FirstOrDefaultAsync(c => c.Id == quotation.CompanyId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Company {quotation.CompanyId} does not exist.");

        var customer = await _db.Customers
            .FirstOrDefaultAsync(c => c.Id == quotation.CustomerId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Customer {quotation.CustomerId} does not exist.");

        var rounding = BusinessRules.RoundPerDocument.Equals(
            await _rules.ResolveAsync(company.Id, BusinessRules.VatRoundingMode, cancellationToken).ConfigureAwait(false),
            StringComparison.Ordinal)
            ? TaxRounding.PerDocument
            : TaxRounding.PerLine;

        // Moving the date re-rates the quote at the rate in force then; leaving it alone keeps the one it
        // was quoted under. Simpler than the invoice's equivalent because a quotation posts nothing — no
        // ledger entry and no stock movement — so there is nothing to move with it. A spent (converted)
        // quote never reaches here: the guard above refuses the edit outright.
        TaxCalculationResult calc;

        if (request.Date is { } moved && moved != quotation.Date)
        {
            var rates = await _db.TaxRates
                .Where(r => r.CompanyId == company.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            calc = _tax.Calculate(new TaxCalculationRequest(
                moved, company.IsVatRegistered, rounding,
                [.. request.Lines.Select(l => new TaxLineInput(l.Quantity, l.UnitPrice, l.DiscountPercent))],
                rates, request.DocumentDiscountPercent));

            quotation.Date = moved;
            quotation.TaxRateId = calc.TaxRateId;
            quotation.TaxRatePercentage = calc.TaxRatePercentage;
        }
        else
        {
            var rateName = quotation.TaxRatePercentage == 0m
                ? "No VAT"
                : $"VAT {quotation.TaxRatePercentage.ToString("0.##", CultureInfo.InvariantCulture)}%";

            calc = _tax.Calculate(new TaxCalculationRequest(
                quotation.Date, company.IsVatRegistered, rounding,
                [.. request.Lines.Select(l => new TaxLineInput(l.Quantity, l.UnitPrice, l.DiscountPercent))],
                AvailableRates: [], request.DocumentDiscountPercent,
                RateOverride: new TaxRateOverride(quotation.TaxRateId, rateName, quotation.TaxRatePercentage)));
        }

        ReconcileLines(quotation, request, calc);

        quotation.ContactPerson = request.ContactPerson ?? string.Empty;
        quotation.Validity = request.Validity ?? string.Empty;
        quotation.DiscountPercent = request.DocumentDiscountPercent;
        quotation.DiscountAmount = calc.Totals.Discount;
        quotation.Subtotal = calc.Totals.Subtotal;
        quotation.NetTotal = calc.Totals.Net;
        quotation.TaxAmount = calc.Totals.Tax;
        quotation.Total = calc.Totals.Total;
        quotation.Cost = request.DocumentCost ?? DocumentCostBasis.Of(request.Lines.Select(l => (l.Cost, l.Quantity)));

        UpdateLegacyShadow(quotation);

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var versionNo = await _versions
            .WriteAsync(DocumentTypes.Quotation, quotation.Id, quotation.CompanyId, Snapshot(quotation, calc), _change.Reason, cancellationToken)
            .ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return new QuotationEdited(quotation.Id, quotation.Number, quotation.Total, versionNo);
    }

    /// <summary>Reconciles the lines in place — update / add / soft-delete. No stock (a quotation issues none).</summary>
    private void ReconcileLines(Quotation quotation, EditQuotation request, TaxCalculationResult calc)
    {
        var existing = quotation.Lines.Where(l => l.DeletedAt is null).ToDictionary(l => l.Id);
        var kept = new HashSet<long>();

        foreach (var (input, line) in request.Lines.Zip(calc.Lines))
        {
            if (input.Id is { } id && existing.TryGetValue(id, out var current))
            {
                current.ItemId = input.ItemId;
                // NOT NULL in quotation_l; see the note in QuotationCreator.
                current.ItemCode = input.ItemCode ?? string.Empty;
                current.Description = input.Description;
                current.Quantity = line.Quantity;
                current.UnitPrice = line.UnitPrice;
                current.DiscountPercent = line.DiscountPercent;
                current.Gross = line.Gross;
                current.Net = line.Net;
                current.Cost = input.Cost;
                SetLineShadow(current, quotation.Number);
                kept.Add(id);
            }
            else
            {
                var added = new QuotationLine
                {
                    QuotationId = quotation.Id,
                    ItemId = input.ItemId,
                    ItemCode = input.ItemCode ?? string.Empty,
                    Description = input.Description,
                    Quantity = line.Quantity,
                    UnitPrice = line.UnitPrice,
                    DiscountPercent = line.DiscountPercent,
                    Gross = line.Gross,
                    Net = line.Net,
                    Cost = input.Cost,
                };
                quotation.Lines.Add(added);
                SetLineShadow(added, quotation.Number);
            }
        }

        foreach (var line in existing.Values.Where(l => !kept.Contains(l.Id)))
        {
            _db.QuotationLines.Remove(line);
        }
    }

    /// <summary>
    /// Refreshes every legacy shadow column an edit can change.
    /// </summary>
    /// <remarks>
    /// <b>The list reads these columns, not the typed ones</b> — <c>QuotationsController.List</c> both
    /// orders and displays from <c>qdate</c>. So a shadow this method forgets is not a tidiness problem
    /// in a column nobody reads; it is the wrong value on the screen everybody starts from.
    /// <para>
    /// <c>qdate</c> was forgotten on the assumption that a document's date is fixed at creation. It is
    /// not — an edit may move it, and moving it re-rates the quotation, so <c>vper</c> goes stale with
    /// it. STQ-223 was edited from 2028-05-19 to 2025-05-19: the detail screen and the new version showed
    /// the new date, and the list went on showing the old one and sorting by it.
    /// </para>
    /// </remarks>
    private void UpdateLegacyShadow(Quotation quotation)
    {
        var entry = _db.Entry(quotation);
        void Set(string name, string? value) => entry.Property(name).CurrentValue = value;

        var hasItem = quotation.Lines.Any(l => l.DeletedAt is null && l.ItemId is not null);

        Set(QuotationLegacyShadow.It, hasItem ? "ITEM" : "SERVICE");
        Set(QuotationLegacyShadow.QDate, quotation.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Set(QuotationLegacyShadow.VPer, Money(quotation.TaxRatePercentage));
        Set(QuotationLegacyShadow.TotAmount, Money(quotation.Total));
        Set(QuotationLegacyShadow.NoVatTotal, Money(quotation.NetTotal));
        Set(QuotationLegacyShadow.QuoteCost, Money(quotation.Cost));
        Set(QuotationLegacyShadow.DiscountPer, Money(quotation.DiscountPercent));
        Set(QuotationLegacyShadow.BeforeDiscTot, Money(quotation.Subtotal));
    }

    private void SetLineShadow(QuotationLine line, string number)
    {
        var entry = _db.Entry(line);
        entry.Property(QuotationLineLegacyShadow.Qno).CurrentValue = number;
        entry.Property(QuotationLineLegacyShadow.Qty).CurrentValue = Money(line.Quantity);
        entry.Property(QuotationLineLegacyShadow.Rate).CurrentValue = Money(line.UnitPrice);
        entry.Property(QuotationLineLegacyShadow.Total).CurrentValue = Money(line.Gross);
    }

    private static string Money(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    private static object Snapshot(Quotation quotation, TaxCalculationResult calc) => new
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
        lines = quotation.Lines.Where(l => l.DeletedAt is null).Select(l => new
        {
            l.ItemId, l.ItemCode, l.Description, l.Quantity, l.UnitPrice, l.DiscountPercent, l.Gross, l.Net, l.Cost,
        }),
    };
}
