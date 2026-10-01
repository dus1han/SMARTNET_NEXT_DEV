using System.Globalization;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using Smartnet.Domain.Auditing;
using Smartnet.Domain.Documents;
using Smartnet.Domain.Reporting;
using Smartnet.Domain.Settings;
using Smartnet.Infrastructure.Persistence;

namespace Smartnet.Infrastructure.Pdf;

/// <inheritdoc cref="IDraftDocumentRenderer"/>
/// <remarks>
/// The same templates a raised quotation or invoice prints with, given <see cref="HouseDocument.IsDraft"/>,
/// and the same valuation the creators run — company rate at the document's date, the company's rounding
/// rule, the document discount — so the totals on a printed draft are the totals it would be raised at.
/// What a draft cannot have is printed as such: the number reads DRAFT, and nothing is paid on an invoice
/// that does not exist.
/// </remarks>
public sealed class DraftDocumentRenderer : IDraftDocumentRenderer
{
    static DraftDocumentRenderer() => QuestPDF.Settings.License = LicenseType.Community;

    /// <summary>What a draft prints in place of the number it does not have yet.</summary>
    private const string NoNumber = "DRAFT";

    private readonly SmartnetDbContext _db;
    private readonly ITaxEngine _tax;
    private readonly IBusinessRuleReader _rules;
    private readonly IChangeContext _change;

    public DraftDocumentRenderer(SmartnetDbContext db, ITaxEngine tax, IBusinessRuleReader rules, IChangeContext change)
    {
        _db = db;
        _tax = tax;
        _rules = rules;
        _change = change;
    }

    public async Task<byte[]?> RenderQuotationAsync(DraftQuotationPrint draft, CancellationToken cancellationToken = default)
    {
        if (await ResolveAsync(draft.CompanyId, draft.CustomerId, draft.Date, draft.DocumentDiscountPercent, draft.Lines, cancellationToken)
                .ConfigureAwait(false) is not { } r)
        {
            return null;
        }

        var vatRegistered = r.Company.IsVatRegistered && r.Calc.TaxRatePercentage > 0m;

        var model = new QuotationModel(
            Logo: r.Logo,
            CompanyName: Trim(r.Company.Name),
            CompanyContact: CompanyHeader.Build(r.Company),
            AccentColour: CompanyTheme.AccentFor(r.Company.Id),
            QuotationNo: NoNumber,
            // The quotation template's own date style — see QuotationRenderer.
            Date: draft.Date.ToString("dd MMM yyyy", CultureInfo.InvariantCulture),
            ClientName: Trim(r.Customer?.Name),
            ClientAddress: Trim(r.Customer?.Address),
            ContactPerson: Trim(draft.ContactPerson),
            PreparedBy: r.PreparedBy,
            Validity: Trim(draft.Validity),
            Items: Items(draft.Lines, r.Calc),
            Subtotal: r.Calc.Totals.Subtotal,
            DiscountPercent: draft.DocumentDiscountPercent,
            DiscountAmount: r.Calc.Totals.Discount,
            NetTotal: r.Calc.Totals.Net,
            TaxLabel: vatRegistered ? $"VAT ({InvoiceRenderer.Percentage(r.Calc.TaxRatePercentage)}%)" : null,
            TaxAmount: vatRegistered ? r.Calc.Totals.Tax : null,
            Total: r.Calc.Totals.Total,
            Bank: InvoiceRenderer.BuildBank(r.Company));

        return new QuotationDocument(model) { IsDraft = true }.GeneratePdf();
    }

    public async Task<byte[]?> RenderInvoiceAsync(DraftInvoicePrint draft, CancellationToken cancellationToken = default)
    {
        if (await ResolveAsync(draft.CompanyId, draft.CustomerId, draft.Date, draft.DocumentDiscountPercent, draft.Lines, cancellationToken)
                .ConfigureAwait(false) is not { } r)
        {
            return null;
        }

        // The invoice template's own date style — see InvoiceRenderer.FormatDate.
        var date = draft.Date.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture);
        var items = Items(draft.Lines, r.Calc).Select(i => new InvoiceItem(i.ItemNo, i.Description, i.Quantity, i.Rate, i.Total)).ToList();
        var totals = r.Calc.Totals;

        // A draft has been paid nothing: the whole total is what it would ask for.
        IDocument document = r.Company.IsVatRegistered && r.Calc.TaxRatePercentage > 0m
            ? new TaxInvoiceDocument(new TaxInvoiceModel(
                Logo: r.Logo,
                CompanyName: Trim(r.Company.Name),
                CompanyContact: CompanyHeader.Build(r.Company),
                AccentColour: CompanyTheme.AccentFor(r.Company.Id),
                InvoiceNo: NoNumber,
                Date: date,
                DateOfSupply: date,
                Supplier: new TaxParty(
                    InvoiceRenderer.Tin(r.Company.VatNumber),
                    Trim(r.Company.Name),
                    InvoiceRenderer.CompanyAddress(r.Company),
                    CompanyHeader.FormatPhone(Trim(r.Company.Phone))),
                Purchaser: new TaxParty(
                    InvoiceRenderer.Tin(r.Customer?.VatNumber),
                    Trim(r.Customer?.Name),
                    Trim(r.Customer?.Address),
                    CompanyHeader.FormatPhone(Trim(r.Customer?.Phone))),
                ContactPerson: Trim(draft.ContactPerson),
                PoNumber: InvoiceRenderer.PoNumber(draft.PurchaseOrderNo),
                PreparedBy: r.PreparedBy,
                Items: items,
                Subtotal: totals.Subtotal,
                DiscountPercent: draft.DocumentDiscountPercent,
                DiscountAmount: totals.Discount,
                NetTotal: totals.Net,
                TaxLabel: $"VAT ({InvoiceRenderer.Percentage(r.Calc.TaxRatePercentage)}%)",
                TaxAmount: totals.Tax,
                Total: totals.Total,
                Paid: 0m,
                BalanceDue: totals.Total,
                Bank: InvoiceRenderer.BuildBank(r.Company))) { IsDraft = true }
            : new InvoiceDocument(new InvoiceModel(
                Logo: r.Logo,
                CompanyName: Trim(r.Company.Name),
                CompanyContact: CompanyHeader.Build(r.Company),
                AccentColour: CompanyTheme.AccentFor(r.Company.Id),
                InvoiceNo: NoNumber,
                Date: date,
                InvoiceType: Trim(draft.Type),
                PoNumber: InvoiceRenderer.PoNumber(draft.PurchaseOrderNo),
                ClientName: Trim(r.Customer?.Name),
                ClientAddress: Trim(r.Customer?.Address),
                ContactPerson: InvoiceRenderer.WithPhone(draft.ContactPerson, r.Customer?.Phone),
                PreparedBy: r.PreparedBy,
                Items: items,
                Subtotal: totals.Subtotal,
                DiscountPercent: draft.DocumentDiscountPercent,
                DiscountAmount: totals.Discount,
                NetTotal: totals.Net,
                Total: totals.Total,
                Paid: 0m,
                BalanceDue: totals.Total,
                Bank: InvoiceRenderer.BuildBank(r.Company))) { IsDraft = true };

        return document.GeneratePdf();
    }

    /// <summary>
    /// The company, customer, logo and author, and the lines valued exactly as the creators value them.
    /// Null when the company does not exist.
    /// </summary>
    /// <exception cref="TaxRateNotResolvableException">The company has no rate in force on the date.</exception>
    private async Task<Resolved?> ResolveAsync(
        long companyId,
        long? customerId,
        DateOnly date,
        decimal documentDiscountPercent,
        IReadOnlyList<DraftPrintLine> lines,
        CancellationToken cancellationToken)
    {
        var company = await _db.Companies
            .FirstOrDefaultAsync(c => c.Id == companyId, cancellationToken)
            .ConfigureAwait(false);

        if (company is null)
        {
            return null;
        }

        // A draft may not have a customer yet; it prints with the Bill To block blank rather than not at all.
        var customer = customerId is { } cid
            ? await _db.Customers
                .Where(c => c.Id == cid)
                .Select(c => new DraftCustomer(c.Name, c.Address, c.Phone, c.VatNumber))
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false)
            : null;

        var rates = await _db.TaxRates
            .Where(r => r.CompanyId == companyId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var rounding = BusinessRules.RoundPerDocument.Equals(
            await _rules.ResolveAsync(companyId, BusinessRules.VatRoundingMode, cancellationToken).ConfigureAwait(false),
            StringComparison.Ordinal)
            ? TaxRounding.PerDocument
            : TaxRounding.PerLine;

        var calc = _tax.Calculate(new TaxCalculationRequest(
            date,
            company.IsVatRegistered,
            rounding,
            [.. lines.Select(l => new TaxLineInput(l.Quantity, l.UnitPrice, l.DiscountPercent))],
            rates,
            documentDiscountPercent));

        var logo = await _db.CompanyLogos
            .Where(l => l.CompanyId == companyId)
            .Select(l => l.Data)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        // Whoever is printing it — the person who would raise it.
        var preparedBy = _change.UserId is { } userId
            ? await _db.Users
                .Where(u => u.Id == userId)
                .Select(u => u.Name ?? u.Username)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false)
            : null;

        return new Resolved(company, customer, logo is { Length: > 0 } ? logo : null, Trim(preparedBy), calc);
    }

    /// <summary>The printed lines: each one's own description and figures, its total after its discount.</summary>
    private static List<QuotationItem> Items(IReadOnlyList<DraftPrintLine> lines, TaxCalculationResult calc) =>
        [.. lines.Zip(calc.Lines, (input, line) =>
            new QuotationItem(string.Empty, Trim(input.Description), line.Quantity, line.UnitPrice, line.Net))];

    private static string Trim(string? s) => s?.Trim() ?? string.Empty;

    private sealed record DraftCustomer(string? Name, string? Address, string? Phone, string? VatNumber);

    private sealed record Resolved(
        Company Company,
        DraftCustomer? Customer,
        byte[]? Logo,
        string PreparedBy,
        TaxCalculationResult Calc);
}
