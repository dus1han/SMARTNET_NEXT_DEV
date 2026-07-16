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

/// <inheritdoc cref="ILegacyInvoiceAdopter"/>
public sealed class LegacyInvoiceAdopter : ILegacyInvoiceAdopter
{
    private readonly SmartnetDbContext _db;
    private readonly ITaxEngine _tax;
    private readonly IDocumentVersionWriter _versions;
    private readonly IBusinessRuleReader _rules;

    public LegacyInvoiceAdopter(
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

    public async Task<long> AdoptAsync(long invoiceId, CancellationToken cancellationToken = default)
    {
        await using var transaction = await _db.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        var invoice = await _db.Invoices
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(i => i.Id == invoiceId && i.DeletedAt == null, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Invoice {invoiceId} does not exist.");

        await MaterialiseInCurrentTransactionAsync(invoice, cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return invoice.Id;
    }

    public async Task MaterialiseInCurrentTransactionAsync(Invoice invoice, CancellationToken cancellationToken = default)
    {
        // Idempotent — a document the new app already owns needs no adoption.
        if (string.Equals(invoice.DataOrigin, "new", StringComparison.Ordinal))
        {
            return;
        }

        var entry = _db.Entry(invoice);
        string? Shadow(string name) => entry.Property(name).CurrentValue as string;

        if (invoice.CompanyId is not { } companyId)
        {
            throw new LegacyDocumentNotAdoptableException(invoice.Number, "it has no company.");
        }

        var company = await _db.Companies
            .FirstOrDefaultAsync(c => c.Id == companyId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new LegacyDocumentNotAdoptableException(invoice.Number, "its company no longer exists.");

        // The customer must be in the master — the new model keys the customer (and the ledger) by id.
        var customerCode = Shadow(InvoiceLegacyShadow.Customer);
        var customer = customerCode is null
            ? null
            : await _db.Customers.FirstOrDefaultAsync(c => c.Code == customerCode, cancellationToken).ConfigureAwait(false);
        if (customer is null)
        {
            throw new LegacyDocumentNotAdoptableException(invoice.Number, $"its customer '{customerCode}' is not in the customer master.");
        }

        var date = LegacyValue.Date(Shadow(InvoiceLegacyShadow.InDate))
            ?? throw new LegacyDocumentNotAdoptableException(invoice.Number, "its date could not be read.");

        var vper = LegacyValue.Money(Shadow(InvoiceLegacyShadow.VPer));
        var documentDiscount = LegacyValue.Money(Shadow(InvoiceLegacyShadow.DiscountPer));
        var type = ParseType(Shadow(InvoiceLegacyShadow.InvType));

        // The lines are the legacy invoice_l rows, joined by the number (they are not linked to a surrogate
        // header id yet — that is what adoption does). Load them as entities so we can materialise them.
        var lineEntities = await _db.InvoiceLines
            .IgnoreQueryFilters()
            .Where(l => EF.Property<string>(l, InvoiceLineLegacyShadow.Inno) == invoice.Number && l.DeletedAt == null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (lineEntities.Count == 0)
        {
            throw new LegacyDocumentNotAdoptableException(invoice.Number, "it has no lines to value.");
        }

        // Resolve item codes so a line whose item still exists carries its id and cost (and can issue/return
        // stock later); a line whose code is gone becomes a free-text service line.
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

        // Each legacy line's quantity and rate come from its varchar shadow columns.
        var lineInputs = lineEntities
            .Select(l => (
                Entity: l,
                Quantity: LegacyValue.Money(_db.Entry(l).Property(InvoiceLineLegacyShadow.Qty).CurrentValue as string),
                Rate: LegacyValue.Money(_db.Entry(l).Property(InvoiceLineLegacyShadow.Rate).CurrentValue as string)))
            .ToList();

        var rounding = BusinessRules.RoundPerDocument.Equals(
            await _rules.ResolveAsync(companyId, BusinessRules.VatRoundingMode, cancellationToken).ConfigureAwait(false),
            StringComparison.Ordinal)
            ? TaxRounding.PerDocument
            : TaxRounding.PerLine;

        // Recompute the money through the decimal engine at the invoice's stored rate — the figure the new
        // app stands behind. No per-line discount in legacy data; the document discount is the stored rate.
        var rateName = vper == 0m ? "No VAT" : $"VAT {vper.ToString("0.##", CultureInfo.InvariantCulture)}%";
        var calc = _tax.Calculate(new TaxCalculationRequest(
            date,
            company.IsVatRegistered,
            rounding,
            [.. lineInputs.Select(l => new TaxLineInput(l.Quantity, l.Rate, 0m))],
            AvailableRates: [],
            documentDiscount,
            RateOverride: new TaxRateOverride(null, rateName, vper)));

        // --- Populate the header's typed columns from the legacy data + the recompute -------------------
        invoice.CustomerId = customer.Id;
        invoice.Date = date;
        invoice.Type = type;
        invoice.DiscountPercent = documentDiscount;
        invoice.DiscountAmount = calc.Totals.Discount;
        invoice.Subtotal = calc.Totals.Subtotal;
        invoice.NetTotal = calc.Totals.Net;
        invoice.TaxRateId = null;
        invoice.TaxRatePercentage = vper;
        invoice.TaxAmount = calc.Totals.Tax;
        invoice.Total = calc.Totals.Total;
        invoice.DataOrigin = "new"; // adopted — the new app now owns it

        // --- Materialise the lines: link to the header, populate the typed columns ----------------------
        decimal totalCost = 0m;
        foreach (var ((entity, _, _), line) in lineInputs.Zip(calc.Lines))
        {
            var matched = entity.ItemCode is not null && items.TryGetValue(entity.ItemCode, out var item);
            var cost = matched ? items[entity.ItemCode!].Cost : (decimal?)null;

            entity.InvoiceId = invoice.Id;
            entity.ItemId = matched ? items[entity.ItemCode!].Id : null;
            entity.Quantity = line.Quantity;
            entity.UnitPrice = line.UnitPrice;
            entity.DiscountPercent = 0m;
            entity.Gross = line.Gross;
            entity.Net = line.Net;
            entity.Cost = cost;
            totalCost += cost ?? 0m;

            if (!invoice.Lines.Contains(entity))
            {
                invoice.Lines.Add(entity);
            }
        }

        invoice.Cost = totalCost;

        // The version-1 "as imported" snapshot, and the save that persists the materialisation. The
        // concurrency check fires here against the row_version the caller set (an edit/void of a stale copy
        // is rejected). The invoice keeps its OPENING_BALANCE ledger entry — adoption changes what the
        // document is, not what is owed.
        await _versions
            .WriteAsync(DocumentTypes.Invoice, invoice.Id, companyId, Snapshot(invoice, calc, customer, company),
                reason: "Adopted from the legacy system", cancellationToken)
            .ConfigureAwait(false);
    }

    private static InvoiceType ParseType(string? invtype) =>
        invtype is not null && invtype.Contains("cash", StringComparison.OrdinalIgnoreCase)
            ? InvoiceType.Cash
            : InvoiceType.Credit;

    private static object Snapshot(Invoice invoice, TaxCalculationResult calc, Customer customer, Company company) => new
    {
        adopted = true,
        invoice = new
        {
            invoice.Number,
            invoice.Date,
            Type = invoice.Type.ToString(),
            invoice.PurchaseOrderNo,
            invoice.ContactPerson,
            invoice.Subtotal,
            invoice.DiscountAmount,
            invoice.NetTotal,
            invoice.TaxAmount,
            invoice.Total,
            invoice.Cost,
            tax = new { calc.TaxRateId, calc.TaxRateName, calc.TaxRatePercentage },
        },
        customer = new { customer.Id, customer.Code, customer.Name, customer.VatNumber },
        company = new { company.Id, company.Name, company.VatNumber },
        lines = invoice.Lines.Where(l => l.DeletedAt is null).Select(l => new
        {
            l.ItemId,
            l.ItemCode,
            l.Description,
            l.Quantity,
            l.UnitPrice,
            l.Gross,
            l.Net,
            l.Cost,
        }),
    };
}
