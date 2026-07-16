using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Smartnet.Api.Auth;
using Smartnet.Api.Contracts;
using Smartnet.Domain.Documents;
using Smartnet.Domain.Identity;
using Smartnet.Infrastructure.Persistence;

namespace Smartnet.Api.Controllers;

/// <summary>
/// Invoices — the documents engine's first writer (Phase 5, slice 1).
/// </summary>
/// <remarks>
/// One controller for what the legacy app split across four (item vs service, create vs edit): a line
/// either references an item or is free-typed, and that is the whole difference. The browser holds the
/// draft and posts it whole — the server-session cart is gone (D4). The save is one transaction behind
/// <see cref="IInvoiceCreator"/>: number, header, lines, ledger, stock and snapshot, all or none.
/// </remarks>
[ApiController]
[Route("api/invoices")]
public sealed class InvoicesController : ControllerBase
{
    private readonly IInvoiceCreator _creator;
    private readonly ICompanyContext _company;
    private readonly SmartnetDbContext _db;
    private readonly ITaxEngine _tax;

    public InvoicesController(IInvoiceCreator creator, ICompanyContext company, SmartnetDbContext db, ITaxEngine tax)
    {
        _creator = creator;
        _company = company;
        _db = db;
        _tax = tax;
    }

    /// <summary>The invoices this app has raised, newest first, across the companies the caller may see.</summary>
    [HttpGet]
    [RequirePermission(Permissions.SearchInvoice)]
    public async Task<ActionResult<IReadOnlyList<InvoiceSummary>>> List(CancellationToken cancellationToken)
    {
        var accessible = _company.Accessible.ToList();

        var invoices = await _db.Invoices
            .Where(i => i.CompanyId != null && accessible.Contains(i.CompanyId.Value))
            .OrderByDescending(i => i.Id)
            .Select(i => new { i.Id, i.Number, i.Date, i.CustomerId, i.Type, i.Total })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var customerIds = invoices.Select(i => i.CustomerId).Distinct().ToList();
        var names = await _db.Customers
            .Where(c => customerIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken)
            .ConfigureAwait(false);

        // Outstanding is derived, per invoice, from the ledger — never a stored column (B3).
        var invoiceIds = invoices.Select(i => i.Id).ToList();
        var outstanding = await _db.ReceivablesLedger
            .Where(e => e.InvoiceId != null && invoiceIds.Contains(e.InvoiceId.Value))
            .GroupBy(e => e.InvoiceId!.Value)
            .Select(g => new { InvoiceId = g.Key, Balance = g.Sum(e => e.Amount) })
            .ToDictionaryAsync(x => x.InvoiceId, x => x.Balance, cancellationToken)
            .ConfigureAwait(false);

        return Ok(invoices.Select(i => new InvoiceSummary(
            i.Id,
            i.Number,
            i.Date,
            names.GetValueOrDefault(i.CustomerId),
            i.Type.ToString(),
            i.Total,
            outstanding.GetValueOrDefault(i.Id))).ToList());
    }

    /// <summary>One invoice in full — the read view.</summary>
    [HttpGet("{id:long}")]
    [RequirePermission(Permissions.SearchInvoice)]
    public async Task<ActionResult<InvoiceDetail>> Get(long id, CancellationToken cancellationToken)
    {
        var accessible = _company.Accessible.ToList();

        var invoice = await _db.Invoices
            .Include(i => i.Lines)
            .FirstOrDefaultAsync(
                i => i.Id == id && i.CompanyId != null && accessible.Contains(i.CompanyId.Value),
                cancellationToken)
            .ConfigureAwait(false);

        if (invoice is null)
        {
            return NotFound();
        }

        var customer = await _db.Customers
            .FirstOrDefaultAsync(c => c.Id == invoice.CustomerId, cancellationToken)
            .ConfigureAwait(false);

        var outstanding = await _db.ReceivablesLedger
            .Where(e => e.InvoiceId == id)
            .SumAsync(e => e.Amount, cancellationToken)
            .ConfigureAwait(false);

        return Ok(new InvoiceDetail(
            invoice.Id,
            invoice.Number,
            invoice.Date,
            invoice.Type.ToString(),
            customer?.Name,
            customer?.Code,
            invoice.PurchaseOrderNo,
            invoice.ContactPerson,
            invoice.Subtotal,
            invoice.DiscountAmount,
            invoice.NetTotal,
            invoice.TaxRatePercentage,
            invoice.TaxAmount,
            invoice.Total,
            outstanding,
            [.. invoice.Lines.Select(l => new InvoiceLineDetail(
                l.ItemId, l.ItemCode, l.Description, l.Quantity, l.UnitPrice, l.DiscountPercent, l.Gross, l.Net, l.Cost))]));
    }

    /// <summary>
    /// The VAT rate a new invoice would carry for a company on a date — one rate per document.
    /// </summary>
    /// <remarks>
    /// The New Invoice screen holds the draft and posts it whole, so it never round-trips a line while the
    /// user types; but to show the VAT rate and value as they build the document, it needs the rate once.
    /// Resolved by the one tax engine (with no lines — only the rate is wanted), so the preview matches the
    /// figure <see cref="Create"/> will charge. Gated by <see cref="Permissions.ItemInvoice"/>: the person
    /// raising the invoice, not a settings administrator.
    /// </remarks>
    [HttpGet("tax-rate")]
    [RequirePermission(Permissions.ItemInvoice)]
    public async Task<ActionResult<InvoiceTaxRate>> TaxRate(
        long companyId,
        DateOnly date,
        CancellationToken cancellationToken)
    {
        if (!_company.Accessible.Contains(companyId))
        {
            return Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "You cannot raise an invoice in that company.");
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
            // A VAT-registered company with no rate configured for that date is a misconfiguration the
            // caller can act on (set a rate, or check the date), and it is the same reason the save would
            // fail — surfaced here so the screen warns before they type a whole document.
            return Problem(statusCode: StatusCodes.Status409Conflict, title: notInForce.Message);
        }
    }

    /// <remarks>
    /// Creating is not a reason-gated action (AUDIT.md §5 — "the record is the reason"), so no
    /// <c>X-Change-Reason</c> here; editing an issued invoice, in slice 5, is.
    /// </remarks>
    [HttpPost]
    [RequirePermission(Permissions.ItemInvoice)]
    public async Task<ActionResult<InvoiceCreatedResponse>> Create(
        CreateInvoiceRequest request,
        CancellationToken cancellationToken)
    {
        // Deny by default extends to the company: a caller may only raise a document in a company their
        // token grants, never one they named in the body but cannot see.
        if (!_company.Accessible.Contains(request.CompanyId))
        {
            return Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "You cannot raise an invoice in that company.");
        }

        var type = request.Type == "Cash" ? InvoiceType.Cash : InvoiceType.Credit;

        InvoiceCreated created;
        try
        {
            created = await _creator.CreateAsync(
                new NewInvoice(
                    request.CompanyId,
                    request.CustomerId,
                    type,
                    request.Date,
                    request.PurchaseOrderNo,
                    request.ContactPerson,
                    [.. request.Lines.Select(l => new NewInvoiceLine(
                        l.ItemId, l.ItemCode, l.Description, l.Quantity, l.UnitPrice, l.DiscountPercent, l.Cost))],
                    request.DocumentDiscountPercent),
                cancellationToken).ConfigureAwait(false);
        }
        catch (CreditLimitExceededException over)
        {
            // A business rule the caller can act on (raise the limit, take a payment first), not a
            // server fault — 409, with the numbers, not a generic 500.
            return Problem(statusCode: StatusCodes.Status409Conflict, title: over.Message);
        }

        return Ok(new InvoiceCreatedResponse(created.Id, created.Number, created.Total, created.Outstanding));
    }
}
