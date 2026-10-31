using Microsoft.EntityFrameworkCore;
using Smartnet.Domain.Auditing;
using Smartnet.Domain.Documents;
using Smartnet.Domain.Settings;
using Smartnet.Infrastructure.Persistence;

namespace Smartnet.Infrastructure.Documents;

/// <inheritdoc cref="IProformaInvoiceService"/>
public sealed class ProformaInvoiceService : IProformaInvoiceService
{
    private readonly SmartnetDbContext _db;
    private readonly SmartnetLegacyDbContext _legacy;
    private readonly ITaxEngine _tax;
    private readonly IDocumentNumberAllocator _numbers;
    private readonly IDocumentVersionWriter _versions;
    private readonly IBusinessRuleReader _rules;
    private readonly IInvoiceCreator _invoices;
    private readonly IChangeContext _change;
    private readonly TimeProvider _time;

    public ProformaInvoiceService(
        SmartnetDbContext db,
        SmartnetLegacyDbContext legacy,
        ITaxEngine tax,
        IDocumentNumberAllocator numbers,
        IDocumentVersionWriter versions,
        IBusinessRuleReader rules,
        IInvoiceCreator invoices,
        IChangeContext change,
        TimeProvider time)
    {
        _db = db;
        _legacy = legacy;
        _tax = tax;
        _numbers = numbers;
        _versions = versions;
        _rules = rules;
        _invoices = invoices;
        _change = change;
        _time = time;
    }

    public async Task<ProformaCreated> CreateFromQuotationAsync(long quotationId, DateOnly proformaDate, CancellationToken cancellationToken = default)
    {
        // One transaction: the number, the proforma and its snapshot commit together or not at all — so a
        // failed save hands its number back rather than leaving a gap in the company's sequence.
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        // Taken first, so a concurrent conversion of the same quote waits and then sees this proforma.
        await QuotationLock.TakeAsync(_db, quotationId, cancellationToken).ConfigureAwait(false);

        // IgnoreQueryFilters so a legacy quotation — which is every quotation on live — loads too.
        var quotation = await _db.Quotations
            .IgnoreQueryFilters()
            .Include(q => q.Lines)
            .FirstOrDefaultAsync(q => q.Id == quotationId && q.DeletedAt == null, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Quotation {quotationId} does not exist.");

        if (quotation.ConvertedToInvoiceId is { } invoiceId)
        {
            throw new QuotationAlreadyConvertedException(quotation.Number, invoiceId);
        }

        var live = await LiveProformaNumberAsync(quotationId, cancellationToken).ConfigureAwait(false);
        if (live is not null)
        {
            throw new QuotationHasProformaException(quotation.Number, live);
        }

        var content = await QuotationContentReader.ReadAsync(quotation, _db, _legacy, cancellationToken).ConfigureAwait(false);

        var company = await _db.Companies
            .FirstOrDefaultAsync(c => c.Id == content.CompanyId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Company {content.CompanyId} does not exist.");

        var rates = await _db.TaxRates
            .Where(r => r.CompanyId == company.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var rounding = BusinessRules.RoundPerDocument.Equals(
            await _rules.ResolveAsync(company.Id, BusinessRules.VatRoundingMode, cancellationToken).ConfigureAwait(false),
            StringComparison.Ordinal)
            ? TaxRounding.PerDocument
            : TaxRounding.PerLine;

        // Valued at the proforma's own date by the same tax engine every document uses — so what the customer
        // is asked to pay is what the invoice will charge if it is raised on the same day.
        var calc = _tax.Calculate(new TaxCalculationRequest(
            proformaDate,
            company.IsVatRegistered,
            rounding,
            [.. content.Lines.Select(l => new TaxLineInput(l.Quantity, l.UnitPrice, l.DiscountPercent))],
            rates,
            content.DocumentDiscountPercent));

        // The company's own proforma series — each company numbers its proformas separately.
        var number = await _numbers
            .AllocateAsync(company.Id, DocumentTypes.Proforma, proformaDate, cancellationToken)
            .ConfigureAwait(false);

        var proforma = new ProformaInvoice
        {
            Number = number,
            CompanyId = company.Id,
            QuotationId = quotation.Id,
            QuotationNumber = quotation.Number,
            CustomerId = content.CustomerId,
            Date = proformaDate,
            ContactPerson = content.ContactPerson,
            Validity = quotation.Validity,
            PreparedBy = _change.UserId,
            Subtotal = calc.Totals.Subtotal,
            DiscountPercent = content.DocumentDiscountPercent,
            DiscountAmount = calc.Totals.Discount,
            NetTotal = calc.Totals.Net,
            TaxRateId = calc.TaxRateId,
            TaxRatePercentage = calc.TaxRatePercentage,
            TaxAmount = calc.Totals.Tax,
            Total = calc.Totals.Total,
            Lines = [.. content.Lines.Zip(calc.Lines, (input, line) => new ProformaInvoiceLine
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

        _db.ProformaInvoices.Add(proforma);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await _versions
            .WriteAsync(DocumentTypes.Proforma, proforma.Id, company.Id, Snapshot(proforma, calc), reason: null, cancellationToken)
            .ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return new ProformaCreated(proforma.Id, number, proforma.Total);
    }

    public async Task<InvoiceCreated> ConvertToInvoiceAsync(long proformaId, ConvertQuotation request, CancellationToken cancellationToken = default)
    {
        // One transaction: the invoice (number, ledger, stock, snapshot) and the marking of both the proforma
        // and its quotation are one act — never an invoice without the proforma spent, nor the reverse.
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        // A second, concurrent conversion of the same proforma waits here and then finds it converted.
        await _db.Database
            .ExecuteSqlAsync($"SELECT id FROM proforma_invoices WHERE id = {proformaId} FOR UPDATE", cancellationToken)
            .ConfigureAwait(false);

        var proforma = await _db.ProformaInvoices
            .Include(p => p.Lines)
            .FirstOrDefaultAsync(p => p.Id == proformaId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Proforma invoice {proformaId} does not exist.");

        if (proforma.IsConverted)
        {
            throw new ProformaAlreadyConvertedException(proforma.Number);
        }

        var hasItem = proforma.Lines.Any(l => l.ItemId is not null);

        // The invoice is the proforma's content, valued again at the invoice's own date through the ordinary
        // pipeline — so it gets a real number, a ledger charge, a stock issue, the credit-limit check and a
        // snapshot, exactly like any other invoice.
        var created = await _invoices.CreateInCurrentTransactionAsync(
            new NewInvoice(
                proforma.CompanyId,
                proforma.CustomerId,
                request.Type,
                request.Date,
                request.PurchaseOrderNo,
                request.ContactPerson ?? proforma.ContactPerson,
                [.. proforma.Lines.OrderBy(l => l.Id).Select(l => new NewInvoiceLine(
                    l.ItemId, l.ItemCode, l.Description, l.Quantity, l.UnitPrice, l.DiscountPercent, l.Cost))],
                proforma.DiscountPercent,
                // Service work has no cost to derive, so it is entered here — the same rule as a quotation.
                DocumentCost: hasItem
                    ? null
                    : request.DocumentCost ?? throw new ServiceCostRequiredException(proforma.Number, "Proforma invoice")),
            sourceQuotationId: proforma.QuotationId,
            cancellationToken).ConfigureAwait(false);

        var now = _time.GetUtcNow().UtcDateTime;
        proforma.ConvertedToInvoiceId = created.Id;
        proforma.ConvertedAt = now;
        proforma.ConvertedBy = _change.UserId;

        // The quotation is spent too, pointing at the same invoice — so it reads "converted" and can never be
        // converted again by any route.
        var quotation = await _db.Quotations
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(q => q.Id == proforma.QuotationId, cancellationToken)
            .ConfigureAwait(false);
        if (quotation is not null)
        {
            quotation.ConvertedToInvoiceId = created.Id;
            quotation.ConvertedAt = now;
            quotation.ConvertedBy = _change.UserId;
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return created;
    }

    public async Task<ProformaVoided> VoidAsync(long proformaId, int expectedRowVersion, CancellationToken cancellationToken = default)
    {
        var proforma = await _db.ProformaInvoices
            .FirstOrDefaultAsync(p => p.Id == proformaId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Proforma invoice {proformaId} does not exist.");

        // Once it is an invoice, voiding the proforma would leave the invoice pointing at nothing. The invoice
        // is what gets voided then.
        if (proforma.IsConverted)
        {
            throw new ProformaAlreadyConvertedException(proforma.Number);
        }

        _db.Entry(proforma).Property(p => p.RowVersion).OriginalValue = expectedRowVersion;

        // Soft: the proforma stays on record, marked voided, and its quotation is free again.
        proforma.DeletedAt = _time.GetUtcNow().UtcDateTime;
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new ProformaVoided(proforma.Id, proforma.Number);
    }

    /// <summary>The number of the quotation's live (not voided) proforma, or null when it has none.</summary>
    private Task<string?> LiveProformaNumberAsync(long quotationId, CancellationToken cancellationToken) =>
        _db.ProformaInvoices
            .Where(p => p.QuotationId == quotationId)
            .Select(p => p.Number)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>The proforma as issued — resolved, not referenced — so a reprint reproduces it.</summary>
    private static object Snapshot(ProformaInvoice p, TaxCalculationResult calc) => new
    {
        proforma = new
        {
            p.Number,
            p.Date,
            p.QuotationId,
            p.QuotationNumber,
            p.CustomerId,
            p.ContactPerson,
            p.Validity,
            p.Subtotal,
            p.DiscountPercent,
            p.DiscountAmount,
            p.NetTotal,
            p.TaxAmount,
            p.Total,
            tax = new { calc.TaxRateId, calc.TaxRateName, calc.TaxRatePercentage },
        },
        lines = p.Lines.Select(l => new
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
