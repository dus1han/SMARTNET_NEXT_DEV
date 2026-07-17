using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Smartnet.Api.Auditing;
using Smartnet.Api.Auth;
using Smartnet.Api.Contracts;
using Smartnet.Domain.Documents;
using Smartnet.Domain.Identity;
using Smartnet.Infrastructure.Persistence;
using Smartnet.Infrastructure.Reporting;

namespace Smartnet.Api.Controllers;

/// <summary>
/// Customer receipts — the money-in module (Phase 7, slice 1).
/// </summary>
/// <remarks>
/// A receipt is money received from a customer, <b>allocated across one or more open invoices</b> — richer
/// than the legacy one-payment-one-invoice screen. Each allocation is a receivables-ledger <c>Payment</c>
/// entry (the truth, from which the outstanding is derived), dual-writing the legacy <c>payments</c> row and
/// <c>invoice_h.balance</c> for the surviving legacy outstanding report. Idempotent, and voidable through a
/// compensating entry — never by rewriting a balance.
/// </remarks>
[ApiController]
[Route("api/customer-receipts")]
public sealed class CustomerReceiptsController : ControllerBase
{
    private readonly ICustomerReceiptCreator _creator;
    private readonly ICustomerReceiptVoider _voider;
    private readonly ICompanyContext _company;
    private readonly SmartnetDbContext _db;
    private readonly SmartnetLegacyDbContext _legacy;

    public CustomerReceiptsController(
        ICustomerReceiptCreator creator,
        ICustomerReceiptVoider voider,
        ICompanyContext company,
        SmartnetDbContext db,
        SmartnetLegacyDbContext legacy)
    {
        _creator = creator;
        _voider = voider;
        _company = company;
        _db = db;
        _legacy = legacy;
    }

    /// <summary>A customer's open invoices — the picker a receipt is allocated over. New and legacy alike, from the ledger.</summary>
    [HttpGet("outstanding")]
    [RequirePermission(Permissions.Payments)]
    public async Task<ActionResult<IReadOnlyList<OutstandingInvoiceLine>>> Outstanding(
        [FromQuery] long customerId, CancellationToken cancellationToken)
    {
        var accessibleText = _company.Accessible.Select(id => id.ToString(CultureInfo.InvariantCulture)).ToHashSet();

        // Derived outstanding per invoice, in one grouped query over the ledger — unifies new and legacy invoices.
        var ledgerRows = await _db.ReceivablesLedger
            .Where(e => e.CustomerId == customerId && e.InvoiceId != null)
            .GroupBy(e => e.InvoiceId!.Value)
            .Select(g => new { InvoiceId = g.Key, Outstanding = g.Sum(e => e.Amount) })
            .Where(x => x.Outstanding > 0m)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (ledgerRows.Count == 0)
        {
            return Ok(Array.Empty<OutstandingInvoiceLine>());
        }

        var outstanding = ledgerRows.ToDictionary(r => r.InvoiceId, r => r.Outstanding);
        var ids = outstanding.Keys.ToList();

        var invoices = await _legacy.InvoiceHs
            .Where(h => ids.Contains(h.Id))
            .Select(h => new { h.Id, h.Invoiceno, h.Indate, h.Totamount, h.Company, h.DataOrigin })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var lines = invoices
            .Where(h => h.Company != null && accessibleText.Contains(h.Company))
            .Select(h => new OutstandingInvoiceLine(
                h.Id,
                h.Invoiceno ?? string.Empty,
                LegacyValue.Date(h.Indate) ?? DateOnly.MinValue,
                LegacyValue.Money(h.Totamount),
                outstanding[h.Id],
                h.DataOrigin == "new" ? "new" : "legacy"))
            .OrderBy(l => l.Date)
            .ThenBy(l => l.Number)
            .ToList();

        return Ok(lines);
    }

    /// <summary>Every receipt the caller may see, newest first.</summary>
    [HttpGet]
    [RequirePermission(Permissions.Payments)]
    public async Task<ActionResult<IReadOnlyList<CustomerReceiptSummary>>> List(CancellationToken cancellationToken)
    {
        var accessible = _company.Accessible.ToList();

        var receipts = await _db.CustomerReceipts
            .Where(r => r.CompanyId != null && accessible.Contains(r.CompanyId.Value))
            .Select(r => new
            {
                r.Id,
                r.Date,
                r.CustomerId,
                r.Amount,
                r.Method,
                r.Reference,
                Invoices = r.Allocations.Count,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var customerIds = receipts.Select(r => r.CustomerId).Distinct().ToList();
        var names = await _db.Customers
            .Where(c => customerIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken)
            .ConfigureAwait(false);

        var rows = receipts
            .Select(r => new CustomerReceiptSummary(
                r.Id, r.Date, names.GetValueOrDefault(r.CustomerId), r.Amount, r.Method, r.Reference, r.Invoices))
            .OrderByDescending(r => r.Date)
            .ThenByDescending(r => r.Id)
            .ToList();

        return Ok(rows);
    }

    /// <summary>One receipt in full — its per-invoice allocations.</summary>
    [HttpGet("{id:long}")]
    [RequirePermission(Permissions.Payments)]
    public async Task<ActionResult<CustomerReceiptDetail>> Get(long id, CancellationToken cancellationToken)
    {
        var accessible = _company.Accessible.ToList();

        var receipt = await _db.CustomerReceipts
            .Include(r => r.Allocations)
            .FirstOrDefaultAsync(
                r => r.Id == id && r.CompanyId != null && accessible.Contains(r.CompanyId.Value),
                cancellationToken)
            .ConfigureAwait(false);

        if (receipt is null)
        {
            return NotFound();
        }

        var customer = await _db.Customers
            .Where(c => c.Id == receipt.CustomerId)
            .Select(c => new { c.Name, c.Code })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        var companyName = receipt.CompanyId is { } cid
            ? await _db.Companies.Where(c => c.Id == cid).Select(c => c.Name).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            : null;

        var invoiceIds = receipt.Allocations.Select(a => a.InvoiceId).Distinct().ToList();
        var numbers = (await _legacy.InvoiceHs
            .Where(h => invoiceIds.Contains(h.Id))
            .Select(h => new { h.Id, h.Invoiceno })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false))
            .ToDictionary(h => h.Id, h => h.Invoiceno);

        var allocations = receipt.Allocations
            .Select(a => new ReceiptAllocationLine(a.InvoiceId, numbers.GetValueOrDefault(a.InvoiceId), a.Amount))
            .ToList();

        return Ok(new CustomerReceiptDetail(
            receipt.Id, receipt.Date, companyName, customer?.Name, customer?.Code,
            receipt.Amount, receipt.Method, receipt.Reference, receipt.RowVersion, allocations));
    }

    /// <summary>Record a receipt — allocated across open invoices; posts Payment entries and dual-writes the legacy shadow.</summary>
    [HttpPost]
    [RequirePermission(Permissions.Payments)]
    public async Task<ActionResult<CustomerReceiptCreatedResponse>> Create(
        CreateCustomerReceiptRequest request, CancellationToken cancellationToken)
    {
        if (!_company.Accessible.Contains(request.CompanyId))
        {
            return Problem(statusCode: StatusCodes.Status403Forbidden, title: "You cannot record a receipt in that company.");
        }

        try
        {
            var created = await _creator.CreateAsync(
                new NewCustomerReceipt(
                    request.CompanyId, request.CustomerId, request.Date, request.Method, request.Reference,
                    request.IdempotencyKey,
                    request.Allocations.Select(a => new NewReceiptAllocation(a.InvoiceId, a.Amount)).ToList()),
                cancellationToken).ConfigureAwait(false);

            return Ok(new CustomerReceiptCreatedResponse(created.Id, created.Amount, created.AlreadyExisted));
        }
        catch (ReceiptAllocationExceedsOutstandingException over)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: over.Message);
        }
        catch (ReceiptInvoiceCustomerMismatchException mismatch)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: mismatch.Message);
        }
    }

    /// <summary>Void a receipt — soft, reason-gated; reverses each allocation through a compensating entry.</summary>
    [HttpDelete("{id:long}")]
    [RequirePermission(Permissions.Payments)]
    [RequireChangeReason]
    public async Task<IActionResult> Delete(long id, [FromQuery] int expectedRowVersion, CancellationToken cancellationToken)
    {
        var accessible = _company.Accessible.ToList();
        var companyId = await _db.CustomerReceipts
            .IgnoreQueryFilters()
            .Where(r => r.Id == id && r.DeletedAt == null)
            .Select(r => r.CompanyId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (companyId is null || !accessible.Contains(companyId.Value))
        {
            return NotFound();
        }

        try
        {
            await _voider.VoidAsync(id, expectedRowVersion, cancellationToken).ConfigureAwait(false);
            return NoContent();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "This receipt was changed by someone else. Reload and try again.");
        }
    }
}
