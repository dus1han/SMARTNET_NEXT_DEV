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
/// The suspended account — outstanding moved off an invoice so it shows nowhere, and brought back later.
/// </summary>
/// <remarks>
/// New and legacy invoices alike. The money moves in the receivables ledger and the legacy balance shadow
/// (see <see cref="IReceivableSuspensions"/>); this controller lists the account and gates the moves on
/// <see cref="Permissions.ReceivablesSuspend"/> and the caller's companies.
/// </remarks>
[ApiController]
[Route("api/suspended-receivables")]
public sealed class SuspendedReceivablesController : ControllerBase
{
    private readonly IReceivableSuspensions _suspensions;
    private readonly ICompanyContext _company;
    private readonly SmartnetDbContext _db;
    private readonly SmartnetLegacyDbContext _legacy;

    public SuspendedReceivablesController(
        IReceivableSuspensions suspensions,
        ICompanyContext company,
        SmartnetDbContext db,
        SmartnetLegacyDbContext legacy)
    {
        _suspensions = suspensions;
        _company = company;
        _db = db;
        _legacy = legacy;
    }

    /// <summary>The suspended account, newest first. <paramref name="active"/> false lists what has come back.</summary>
    [HttpGet]
    [RequirePermission(Permissions.ReceivablesSuspend)]
    public async Task<ActionResult<IReadOnlyList<SuspendedReceivableSummary>>> List(
        [FromQuery] bool active = true,
        CancellationToken cancellationToken = default)
    {
        var accessible = _company.Accessible.ToList();

        var rows = await _db.SuspendedReceivables
            .Where(s => accessible.Contains(s.CompanyId))
            .Where(s => active
                ? s.Status == SuspendedReceivableStatus.Suspended
                : s.Status != SuspendedReceivableStatus.Suspended)
            .OrderByDescending(s => s.SuspendedOn)
            .ThenByDescending(s => s.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (rows.Count == 0)
        {
            return Ok(Array.Empty<SuspendedReceivableSummary>());
        }

        var invoiceIds = rows.Select(r => r.InvoiceId).Distinct().ToList();
        var invoices = await _legacy.InvoiceHs
            .Where(h => invoiceIds.Contains(h.Id))
            .Select(h => new { h.Id, h.Indate, h.Totamount, h.DataOrigin })
            .ToDictionaryAsync(h => h.Id, cancellationToken)
            .ConfigureAwait(false);

        var customerIds = rows.Select(r => r.CustomerId).Distinct().ToList();
        var customers = await _db.Customers
            .Where(c => customerIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Name, c.Code })
            .ToDictionaryAsync(c => c.Id, cancellationToken)
            .ConfigureAwait(false);

        var companyIds = rows.Select(r => r.CompanyId).Distinct().ToList();
        var companies = await _db.Companies
            .Where(c => companyIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken)
            .ConfigureAwait(false);

        var userIds = rows.Where(r => r.CreatedBy != null).Select(r => r.CreatedBy!.Value).Distinct().ToList();
        var users = await _db.Users
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, Name = u.Name ?? u.Username })
            .ToDictionaryAsync(u => u.Id, u => u.Name, cancellationToken)
            .ConfigureAwait(false);

        return Ok(rows.Select(r =>
        {
            var invoice = invoices.GetValueOrDefault(r.InvoiceId);
            var customer = customers.GetValueOrDefault(r.CustomerId);
            return new SuspendedReceivableSummary(
                r.Id,
                companies.GetValueOrDefault(r.CompanyId),
                r.CustomerId,
                customer?.Name,
                customer?.Code,
                r.InvoiceId,
                r.InvoiceNumber,
                invoice is null ? null : LegacyValue.Date(invoice.Indate),
                invoice is null ? 0m : LegacyValue.Money(invoice.Totamount),
                r.Amount,
                r.SuspendedOn,
                r.Reason,
                r.CreatedBy is { } by ? users.GetValueOrDefault(by) : null,
                r.Status.ToString(),
                r.ReinstatedAt,
                r.ReinstateNote,
                r.ReceiptId,
                r.RowVersion,
                invoice?.DataOrigin == "new" ? "new" : "legacy");
        }).ToList());
    }

    /// <summary>Move part or all of an invoice's outstanding to the suspended account.</summary>
    [HttpPost]
    [RequirePermission(Permissions.ReceivablesSuspend)]
    public async Task<ActionResult<SuspensionCreatedResponse>> Suspend(
        SuspendReceivableRequest request, CancellationToken cancellationToken)
    {
        var company = await _legacy.InvoiceHs
            .Where(h => h.Id == request.InvoiceId)
            .Select(h => h.Company)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!long.TryParse(company, NumberStyles.Integer, CultureInfo.InvariantCulture, out var companyId)
            || !_company.Accessible.Contains(companyId))
        {
            return NotFound();
        }

        try
        {
            var created = await _suspensions.SuspendAsync(
                new SuspendReceivable(request.InvoiceId, request.Amount, request.Date, request.Reason),
                cancellationToken).ConfigureAwait(false);

            return Ok(new SuspensionCreatedResponse(created.Id, created.Amount));
        }
        catch (Exception ex) when (ex is SuspensionExceedsOutstandingException or InvoiceAlreadySuspendedException or InvalidOperationException)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: ex.Message);
        }
    }

    /// <summary>Bring the whole suspended amount back onto its invoice.</summary>
    [HttpPost("{id:long}/reinstate")]
    [RequirePermission(Permissions.ReceivablesSuspend)]
    public async Task<IActionResult> Reinstate(long id, ReinstateSuspensionRequest request, CancellationToken cancellationToken)
    {
        if (!await CallerMaySee(id, cancellationToken).ConfigureAwait(false))
        {
            return NotFound();
        }

        try
        {
            await _suspensions.ReinstateAsync(id, request.RowVersion, request.Note, cancellationToken).ConfigureAwait(false);
            return NoContent();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "This suspension was changed by someone else. Reload and try again.");
        }
        catch (SuspensionNotActiveException gone)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: gone.Message);
        }
    }

    /// <summary>
    /// Bring the suspended amount back and record the payment against the invoice, in one act. Less than the
    /// full amount leaves the rest on the invoice's ordinary outstanding.
    /// </summary>
    [HttpPost("{id:long}/settle")]
    [RequirePermission(Permissions.ReceivablesSuspend)]
    public async Task<ActionResult<CustomerReceiptCreatedResponse>> Settle(
        long id, SettleSuspensionRequest request, CancellationToken cancellationToken)
    {
        if (!await CallerMaySee(id, cancellationToken).ConfigureAwait(false))
        {
            return NotFound();
        }

        try
        {
            var receipt = await _suspensions.SettleAsync(
                id,
                request.RowVersion,
                new SettleSuspension(request.Amount, request.Date, request.Method, request.Reference, request.IdempotencyKey),
                cancellationToken).ConfigureAwait(false);

            return Ok(new CustomerReceiptCreatedResponse(receipt.Id, receipt.Amount, receipt.AlreadyExisted));
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "This suspension was changed by someone else. Reload and try again.");
        }
        catch (Exception ex) when (ex is SuspensionNotActiveException or ReceiptAllocationExceedsOutstandingException or InvalidOperationException)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: ex.Message);
        }
    }

    private async Task<bool> CallerMaySee(long id, CancellationToken cancellationToken)
    {
        var accessible = _company.Accessible.ToList();
        return await _db.SuspendedReceivables
            .AnyAsync(s => s.Id == id && accessible.Contains(s.CompanyId), cancellationToken)
            .ConfigureAwait(false);
    }
}
