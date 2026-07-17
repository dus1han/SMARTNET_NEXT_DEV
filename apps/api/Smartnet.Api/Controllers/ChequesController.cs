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
/// The cheque register (Phase 7, slice 2) — a standalone record of cheques written.
/// </summary>
/// <remarks>
/// No ledger, no balance: the legacy app never tied a cheque to a payment, and neither does this. Adopted
/// additively — this app's cheques (typed columns) and the legacy ones (varchar) share the table. Printing is
/// Phase 8; a cheque is recorded, listed and voided here.
/// </remarks>
[ApiController]
[Route("api/cheques")]
public sealed class ChequesController : ControllerBase
{
    private readonly IChequeCreator _creator;
    private readonly IChequeVoider _voider;
    private readonly ICompanyContext _company;
    private readonly SmartnetDbContext _db;
    private readonly SmartnetLegacyDbContext _legacy;

    public ChequesController(
        IChequeCreator creator,
        IChequeVoider voider,
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

    /// <summary>Every cheque the caller may see, newest first — this app's own and the legacy ones.</summary>
    [HttpGet]
    [RequirePermission(Permissions.Cheques)]
    public async Task<ActionResult<IReadOnlyList<ChequeSummary>>> List(CancellationToken cancellationToken)
    {
        var accessible = _company.Accessible.ToList();
        var accessibleText = accessible.Select(id => id.ToString(CultureInfo.InvariantCulture)).ToHashSet();
        var companyNames = await _db.Companies
            .Where(c => accessible.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken)
            .ConfigureAwait(false);

        var newCheques = await _db.Cheques
            .Where(c => c.CompanyId != null && accessible.Contains(c.CompanyId.Value))
            .Select(c => new { c.Id, c.ChequeDate, c.DueDate, c.PayTo, c.Bank, c.ChequeNumber, c.Amount, c.CompanyId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var rows = newCheques
            .Select(c => new ChequeSummary(
                c.Id, c.ChequeDate, c.DueDate, c.PayTo, c.Bank, c.ChequeNumber, c.Amount,
                c.CompanyId is { } cid ? companyNames.GetValueOrDefault(cid) : null, "new"))
            .ToList();

        var legacyCheques = (await _legacy.Cheques
            .Where(c => c.DataOrigin != "new")
            .Select(c => new { c.Id, c.Chequedate, c.Duedate, c.Payto, c.Bank, c.Chkno, c.Amount, c.Company })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false))
            .Where(c => c.Company != null && accessibleText.Contains(c.Company));

        rows.AddRange(legacyCheques.Select(c => new ChequeSummary(
            c.Id, LegacyValue.Date(c.Chequedate), LegacyValue.Date(c.Duedate), c.Payto, c.Bank, c.Chkno,
            LegacyValue.Money(c.Amount),
            long.TryParse(c.Company, NumberStyles.Integer, CultureInfo.InvariantCulture, out var lc) ? companyNames.GetValueOrDefault(lc) : null,
            "legacy")));

        return Ok(rows
            .OrderByDescending(r => r.ChequeDate ?? DateOnly.MinValue)
            .ThenByDescending(r => r.Id)
            .ToList());
    }

    /// <summary>One cheque in full — this app's own or a legacy one.</summary>
    [HttpGet("{id:long}")]
    [RequirePermission(Permissions.Cheques)]
    public async Task<ActionResult<ChequeDetail>> Get(long id, CancellationToken cancellationToken)
    {
        var accessible = _company.Accessible.ToList();

        var cheque = await _db.Cheques
            .FirstOrDefaultAsync(c => c.Id == id && c.CompanyId != null && accessible.Contains(c.CompanyId.Value), cancellationToken)
            .ConfigureAwait(false);

        if (cheque is not null)
        {
            var companyName = cheque.CompanyId is { } cid
                ? await _db.Companies.Where(c => c.Id == cid).Select(c => c.Name).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false)
                : null;
            var supplierName = cheque.SupplierId is { } sid
                ? await _db.Suppliers.Where(s => s.Id == sid).Select(s => s.Name).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false)
                : null;

            return Ok(new ChequeDetail(
                cheque.Id, cheque.ChequeDate, cheque.DueDate, cheque.PayTo, cheque.EntryType, supplierName, cheque.SupplierCode,
                cheque.Bank, cheque.ChequeNumber, cheque.Amount, companyName, cheque.RowVersion, "new"));
        }

        return await LegacyChequeDetail(id, accessible, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ActionResult<ChequeDetail>> LegacyChequeDetail(long id, List<long> accessible, CancellationToken cancellationToken)
    {
        var accessibleText = accessible.Select(x => x.ToString(CultureInfo.InvariantCulture)).ToHashSet();

        var c = await _legacy.Cheques
            .Where(x => x.Id == (int)id && x.DataOrigin != "new")
            .Select(x => new { x.Id, x.Chequedate, x.Duedate, x.Payto, x.Entry, x.Supcode, x.Bank, x.Chkno, x.Amount, x.Company })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (c is null || c.Company is null || !accessibleText.Contains(c.Company))
        {
            return NotFound();
        }

        var companyName = long.TryParse(c.Company, NumberStyles.Integer, CultureInfo.InvariantCulture, out var cid)
            ? await _db.Companies.Where(x => x.Id == cid).Select(x => x.Name).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            : null;
        var supplierName = string.IsNullOrEmpty(c.Supcode)
            ? null
            : await _db.Suppliers.Where(s => s.Code == c.Supcode).Select(s => s.Name).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        return Ok(new ChequeDetail(
            c.Id, LegacyValue.Date(c.Chequedate), LegacyValue.Date(c.Duedate), c.Payto, c.Entry, supplierName,
            string.IsNullOrEmpty(c.Supcode) ? null : c.Supcode, c.Bank, c.Chkno, LegacyValue.Money(c.Amount),
            companyName, 0, "legacy"));
    }

    /// <summary>Record a cheque — a standalone written record; dual-writes the legacy row for the ChequeReport.</summary>
    [HttpPost]
    [RequirePermission(Permissions.Cheques)]
    public async Task<ActionResult<ChequeCreatedResponse>> Create(CreateChequeRequest request, CancellationToken cancellationToken)
    {
        if (!_company.Accessible.Contains(request.CompanyId))
        {
            return Problem(statusCode: StatusCodes.Status403Forbidden, title: "You cannot record a cheque in that company.");
        }

        var created = await _creator.CreateAsync(
            new NewCheque(
                request.CompanyId, request.EntryType, request.PayTo, request.SupplierId,
                request.Bank, request.ChequeNumber, request.Amount, request.ChequeDate, request.DueDate),
            cancellationToken).ConfigureAwait(false);

        return Ok(new ChequeCreatedResponse(created.Id, created.Amount));
    }

    /// <summary>Void a cheque — soft, reason-gated (not the legacy hard delete).</summary>
    [HttpDelete("{id:long}")]
    [RequirePermission(Permissions.Cheques)]
    [RequireChangeReason]
    public async Task<IActionResult> Delete(long id, [FromQuery] int expectedRowVersion, CancellationToken cancellationToken)
    {
        var accessible = _company.Accessible.ToList();
        var companyId = await _db.Cheques
            .IgnoreQueryFilters()
            .Where(c => c.Id == id && c.DeletedAt == null)
            .Select(c => c.CompanyId)
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
                title: "This cheque was changed by someone else. Reload and try again.");
        }
    }
}
