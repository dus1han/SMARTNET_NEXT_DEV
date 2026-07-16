using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Smartnet.Api.Auth;
using Smartnet.Api.Contracts;
using Smartnet.Domain.Documents;
using Smartnet.Domain.Identity;
using Smartnet.Infrastructure.Persistence;
using Smartnet.Infrastructure.Reporting;

namespace Smartnet.Api.Controllers;

/// <summary>
/// Quotations — the documents engine's second document type (Phase 5, slice 3).
/// </summary>
/// <remarks>
/// The same engine as invoices, given a document that <b>charges nothing and issues nothing</b>: a
/// quotation is a priced offer, so there is no ledger and no stock behind it. What it adds is
/// <see cref="Convert"/> — turning a quote into an invoice through the same save pipeline a hand-keyed
/// invoice uses, marking the quote converted with a back-link, and refusing a second conversion (the
/// legacy copy-paste conversion did none of that — plan §6).
/// </remarks>
[ApiController]
[Route("api/quotations")]
public sealed class QuotationsController : ControllerBase
{
    private readonly IQuotationCreator _creator;
    private readonly IQuotationConverter _converter;
    private readonly ICompanyContext _company;
    private readonly SmartnetDbContext _db;
    private readonly SmartnetLegacyDbContext _legacy;
    private readonly ITaxEngine _tax;

    public QuotationsController(
        IQuotationCreator creator,
        IQuotationConverter converter,
        ICompanyContext company,
        SmartnetDbContext db,
        SmartnetLegacyDbContext legacy,
        ITaxEngine tax)
    {
        _creator = creator;
        _converter = converter;
        _company = company;
        _db = db;
        _legacy = legacy;
        _tax = tax;
    }

    /// <summary>
    /// Every quotation the caller may see, newest first — the ones this app has raised <b>and</b> the ones
    /// adopted from the legacy system (its stored <c>varchar</c> figures, parsed defensively).
    /// </summary>
    [HttpGet]
    [RequirePermission(Permissions.SearchQuotation)]
    public async Task<ActionResult<IReadOnlyList<QuotationSummary>>> List(CancellationToken cancellationToken)
    {
        var accessible = _company.Accessible.ToList();

        // --- New quotations (this app's own) --------------------------------------------------------
        var quotations = await _db.Quotations
            .Where(q => q.CompanyId != null && accessible.Contains(q.CompanyId.Value))
            .Select(q => new { q.Id, q.Number, q.Date, q.CustomerId, q.Total, q.ConvertedToInvoiceId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var customerIds = quotations.Select(q => q.CustomerId).Distinct().ToList();
        var names = await _db.Customers
            .Where(c => customerIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken)
            .ConfigureAwait(false);

        var rows = quotations.Select(q => new QuotationSummary(
            q.Id,
            q.Number,
            q.Date,
            names.GetValueOrDefault(q.CustomerId),
            q.Total,
            q.ConvertedToInvoiceId,
            "new")).ToList();

        // --- Legacy quotations (adopted from the old system) ----------------------------------------
        var accessibleText = accessible.Select(id => id.ToString(CultureInfo.InvariantCulture)).ToHashSet();

        var legacy = await _legacy.QuotationHs
            .Where(h => h.DataOrigin != "new") // legacy rows only
            .Select(h => new { h.Id, h.QNo, h.Qdate, h.Customer, h.Totamount, h.Company, h.ConvertedToInvoiceId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        legacy = legacy.Where(h => h.Company != null && accessibleText.Contains(h.Company)).ToList();

        var legacyCodes = legacy.Select(h => h.Customer).Where(c => c != null).Distinct().ToList();
        var namesByCode = (await _db.Customers
            .Where(c => c.Code != null && legacyCodes.Contains(c.Code))
            .Select(c => new { c.Code, c.Name })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false))
            .ToDictionary(c => c.Code!, c => c.Name);

        rows.AddRange(legacy.Select(h => new QuotationSummary(
            h.Id,
            h.QNo ?? "—",
            LegacyValue.Date(h.Qdate) ?? DateOnly.MinValue,
            h.Customer is not null ? namesByCode.GetValueOrDefault(h.Customer) : null,
            LegacyValue.Money(h.Totamount),
            h.ConvertedToInvoiceId, // set once converted through the new app
            "legacy")));

        return Ok(rows.OrderByDescending(r => r.Date).ThenByDescending(r => r.Id).ToList());
    }

    /// <summary>
    /// One quotation in full — the read view. Serves both a <c>new</c> quotation and a <c>legacy</c> one
    /// adopted from the old system (its stored <c>varchar</c> figures).
    /// </summary>
    [HttpGet("{id:long}")]
    [RequirePermission(Permissions.SearchQuotation)]
    public async Task<ActionResult<QuotationDetail>> Get(long id, CancellationToken cancellationToken)
    {
        var accessible = _company.Accessible.ToList();

        var quotation = await _db.Quotations
            .Include(q => q.Lines)
            .FirstOrDefaultAsync(
                q => q.Id == id && q.CompanyId != null && accessible.Contains(q.CompanyId.Value),
                cancellationToken)
            .ConfigureAwait(false);

        // Not one of this app's own — it may be a legacy quotation adopted into the same table.
        if (quotation is null)
        {
            return await LegacyQuotationDetail(id, accessible, cancellationToken).ConfigureAwait(false);
        }

        var customer = await _db.Customers
            .FirstOrDefaultAsync(c => c.Id == quotation.CustomerId, cancellationToken)
            .ConfigureAwait(false);

        var companyName = quotation.CompanyId is { } cid
            ? await _db.Companies.Where(c => c.Id == cid).Select(c => c.Name).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            : null;

        // If it has been converted, resolve the invoice number for the back-link the read view shows.
        string? convertedInvoiceNumber = null;
        if (quotation.ConvertedToInvoiceId is { } invoiceId)
        {
            convertedInvoiceNumber = await _db.Invoices
                .Where(i => i.Id == invoiceId)
                .Select(i => i.Number)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        var kind = quotation.Lines.Any(l => l.ItemId is not null) ? "Item" : "Service";

        return Ok(new QuotationDetail(
            quotation.Id,
            quotation.Number,
            quotation.Date,
            companyName,
            kind,
            customer?.Name,
            customer?.Code,
            quotation.ContactPerson,
            quotation.Validity,
            quotation.Subtotal,
            quotation.DiscountAmount,
            quotation.NetTotal,
            quotation.TaxRatePercentage,
            quotation.TaxAmount,
            quotation.Total,
            quotation.ConvertedToInvoiceId,
            convertedInvoiceNumber,
            "new",
            [.. quotation.Lines.Select(l => new InvoiceLineDetail(
                null, l.ItemId, l.ItemCode, l.Description, l.Quantity, l.UnitPrice, l.DiscountPercent, l.Gross, l.Net, l.Cost))]));
    }

    /// <summary>
    /// The read view for a legacy quotation — the same shape, built from the old system's <c>varchar</c>
    /// columns, parsed defensively (<see cref="LegacyValue"/>). A legacy quotation has no conversion link.
    /// </summary>
    private async Task<ActionResult<QuotationDetail>> LegacyQuotationDetail(
        long id,
        List<long> accessible,
        CancellationToken cancellationToken)
    {
        var accessibleText = accessible.Select(x => x.ToString(CultureInfo.InvariantCulture)).ToHashSet();

        var h = await _legacy.QuotationHs
            .FirstOrDefaultAsync(x => x.Id == id && x.DataOrigin != "new", cancellationToken)
            .ConfigureAwait(false);

        if (h is null || h.Company is null || !accessibleText.Contains(h.Company))
        {
            return NotFound();
        }

        var lines = await _legacy.QuotationLs
            .Where(l => l.Qno == h.QNo)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var customer = h.Customer is null
            ? null
            : await _db.Customers.FirstOrDefaultAsync(c => c.Code == h.Customer, cancellationToken).ConfigureAwait(false);

        var companyName = long.TryParse(h.Company, NumberStyles.Integer, CultureInfo.InvariantCulture, out var legacyCompanyId)
            ? await _db.Companies.Where(c => c.Id == legacyCompanyId).Select(c => c.Name).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            : null;

        var subtotal = LegacyValue.Money(h.Beforedisctot);
        var net = LegacyValue.Money(h.Novattotal);
        var total = LegacyValue.Money(h.Totamount);
        var kind = string.Equals(h.It, "ITEM", StringComparison.OrdinalIgnoreCase) ? "Item" : "Service";

        // A legacy quote can be converted through the new app, which records the invoice here.
        string? convertedInvoiceNumber = null;
        if (h.ConvertedToInvoiceId is { } invoiceId)
        {
            convertedInvoiceNumber = await _db.Invoices
                .Where(i => i.Id == invoiceId)
                .Select(i => i.Number)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        return Ok(new QuotationDetail(
            h.Id,
            h.QNo ?? "—",
            LegacyValue.Date(h.Qdate) ?? DateOnly.MinValue,
            companyName,
            kind,
            customer?.Name ?? h.Customer,
            customer?.Code ?? h.Customer,
            h.Contactperson,
            h.QValid,
            subtotal,
            subtotal - net,
            net,
            LegacyValue.Money(h.Vper),
            total - net,
            total,
            h.ConvertedToInvoiceId,
            convertedInvoiceNumber,
            "legacy",
            [.. lines.Select(l => new InvoiceLineDetail(
                null,
                null,
                l.Itemcode,
                l.Desc,
                LegacyValue.Money(l.Qty),
                LegacyValue.Money(l.Rate),
                0m,
                LegacyValue.Money(l.Total),
                LegacyValue.Money(l.Total),
                null))]));
    }

    /// <summary>
    /// The single VAT rate a new quotation would carry for a company on a date — the same preview the New
    /// Invoice screen gets, gated by the quotation permission rather than the invoice one.
    /// </summary>
    [HttpGet("tax-rate")]
    [RequirePermission(Permissions.ItemQuotation)]
    public async Task<ActionResult<InvoiceTaxRate>> TaxRate(
        long companyId,
        DateOnly date,
        CancellationToken cancellationToken)
    {
        if (!_company.Accessible.Contains(companyId))
        {
            return Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "You cannot raise a quotation in that company.");
        }

        var company = await _db.Companies
            .FirstOrDefaultAsync(c => c.Id == companyId, cancellationToken)
            .ConfigureAwait(false);
        if (company is null)
        {
            return NotFound();
        }

        var rates = await _db.TaxRates
            .Where(r => r.CompanyId == companyId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            var calc = _tax.Calculate(new TaxCalculationRequest(
                date, company.IsVatRegistered, TaxRounding.PerLine, [], rates));

            return Ok(new InvoiceTaxRate(calc.TaxRateId, calc.TaxRateName, calc.TaxRatePercentage));
        }
        catch (TaxRateNotResolvableException notInForce)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: notInForce.Message);
        }
    }

    /// <summary>Raise a quotation — the whole document, posted once. No ledger, no stock.</summary>
    [HttpPost]
    [RequirePermission(Permissions.ItemQuotation)]
    public async Task<ActionResult<QuotationCreatedResponse>> Create(
        CreateQuotationRequest request,
        CancellationToken cancellationToken)
    {
        if (!_company.Accessible.Contains(request.CompanyId))
        {
            return Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "You cannot raise a quotation in that company.");
        }

        var created = await _creator.CreateAsync(
            new NewQuotation(
                request.CompanyId,
                request.CustomerId,
                request.Date,
                request.ContactPerson,
                request.Validity,
                [.. request.Lines.Select(l => new NewQuotationLine(
                    l.ItemId, l.ItemCode, l.Description, l.Quantity, l.UnitPrice, l.DiscountPercent, l.Cost))],
                request.DocumentDiscountPercent),
            cancellationToken).ConfigureAwait(false);

        return Ok(new QuotationCreatedResponse(created.Id, created.Number, created.Total));
    }

    /// <summary>
    /// Convert a quotation into an invoice — through the same save pipeline, once only.
    /// </summary>
    /// <remarks>
    /// Gated by <see cref="Permissions.ItemInvoice"/>: converting <i>is</i> raising an invoice, so it
    /// takes the invoice-creation right, not merely the quotation one. The credit limit gates it exactly
    /// as it gates a hand-keyed invoice.
    /// </remarks>
    [HttpPost("{id:long}/convert")]
    [RequirePermission(Permissions.ItemInvoice)]
    public async Task<ActionResult<InvoiceCreatedResponse>> Convert(
        long id,
        ConvertQuotationRequest request,
        CancellationToken cancellationToken)
    {
        // The quotation must be one the caller may see; deny by default extends to the company.
        var accessible = _company.Accessible.ToList();
        var quotationCompany = await _db.Quotations
            .Where(q => q.Id == id)
            .Select(q => q.CompanyId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (quotationCompany is null || !accessible.Contains(quotationCompany.Value))
        {
            return NotFound();
        }

        var type = request.Type == "Cash" ? InvoiceType.Cash : InvoiceType.Credit;

        InvoiceCreated created;
        try
        {
            created = await _converter.ConvertAsync(
                id,
                new ConvertQuotation(type, request.Date, request.PurchaseOrderNo, request.ContactPerson),
                cancellationToken).ConfigureAwait(false);
        }
        catch (QuotationAlreadyConvertedException already)
        {
            // Already spent — 409 with the invoice it became, so the caller can open that instead of
            // trying to make a second one.
            return Problem(statusCode: StatusCodes.Status409Conflict, title: already.Message);
        }
        catch (CreditLimitExceededException over)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: over.Message);
        }

        return Ok(new InvoiceCreatedResponse(created.Id, created.Number, created.Total, created.Outstanding));
    }
}
