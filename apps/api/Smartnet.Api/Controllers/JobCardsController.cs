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
/// Job cards — the service/repair module (Phase 6, slice 3).
/// </summary>
/// <remarks>
/// The lightest document: no tax, no ledger, no stock. It tracks work — the fault, the customer's
/// serial-tracked equipment, and a guarded <c>PENDING → CLOSED</c> close that records the cost and sell.
/// Structured serial lines replace the legacy text blob; the blob is still dual-written so the legacy
/// Crystal job sheet keeps printing.
/// </remarks>
[ApiController]
[Route("api/job-cards")]
public sealed class JobCardsController : ControllerBase
{
    private readonly IJobCardCreator _creator;
    private readonly IJobCardWorkflow _workflow;
    private readonly ICompanyContext _company;
    private readonly SmartnetDbContext _db;
    private readonly SmartnetLegacyDbContext _legacy;

    public JobCardsController(
        IJobCardCreator creator,
        IJobCardWorkflow workflow,
        ICompanyContext company,
        SmartnetDbContext db,
        SmartnetLegacyDbContext legacy)
    {
        _creator = creator;
        _workflow = workflow;
        _company = company;
        _db = db;
        _legacy = legacy;
    }

    /// <summary>Every job card the caller may see, newest first — this app's own and the legacy ones.</summary>
    [HttpGet]
    [RequirePermission(Permissions.JobCards)]
    public async Task<ActionResult<IReadOnlyList<JobCardSummary>>> List(CancellationToken cancellationToken)
    {
        var accessible = _company.Accessible.ToList();

        var cards = await _db.JobCards
            .Where(j => j.CompanyId != null && accessible.Contains(j.CompanyId.Value))
            .Select(j => new { j.Id, j.Number, j.Date, j.CustomerId, j.Status })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var customerIds = cards.Select(j => j.CustomerId).Distinct().ToList();
        var names = await _db.Customers
            .Where(c => customerIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken)
            .ConfigureAwait(false);

        var rows = cards.Select(j => new JobCardSummary(
            j.Id, j.Number, j.Date, names.GetValueOrDefault(j.CustomerId), j.Status, "new")).ToList();

        // --- Legacy job cards -----------------------------------------------------------------------
        var accessibleText = accessible.Select(id => id.ToString(CultureInfo.InvariantCulture)).ToHashSet();

        var legacy = await _legacy.JobsMs
            .Where(h => h.DataOrigin != "new")
            .Select(h => new { h.Id, h.Jobno, h.Jdate, h.Customer, h.Jstat, h.Company })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        legacy = legacy.Where(h => accessibleText.Contains(h.Company)).ToList();

        var legacyCodes = legacy.Select(h => h.Customer).Distinct().ToList();
        var namesByCode = (await _db.Customers
            .Where(c => c.Code != null && legacyCodes.Contains(c.Code))
            .Select(c => new { c.Code, c.Name })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false))
            .ToDictionary(c => c.Code!, c => c.Name);

        rows.AddRange(legacy.Select(h => new JobCardSummary(
            h.Id, h.Jobno, LegacyValue.Date(h.Jdate) ?? DateOnly.MinValue,
            namesByCode.GetValueOrDefault(h.Customer), h.Jstat, "legacy")));

        return Ok(rows.OrderByDescending(r => r.Date).ThenByDescending(r => r.Id).ToList());
    }

    /// <summary>One job card in full — its lines and (once closed) cost/sell.</summary>
    [HttpGet("{id:long}")]
    [RequirePermission(Permissions.JobCards)]
    public async Task<ActionResult<JobCardDetail>> Get(long id, CancellationToken cancellationToken)
    {
        var accessible = _company.Accessible.ToList();

        var card = await _db.JobCards
            .Include(j => j.Lines)
            .FirstOrDefaultAsync(
                j => j.Id == id && j.CompanyId != null && accessible.Contains(j.CompanyId.Value),
                cancellationToken)
            .ConfigureAwait(false);

        if (card is null)
        {
            return await LegacyJobCardDetail(id, accessible, cancellationToken).ConfigureAwait(false);
        }

        var customer = await _db.Customers
            .FirstOrDefaultAsync(c => c.Id == card.CustomerId, cancellationToken)
            .ConfigureAwait(false);
        var companyName = card.CompanyId is { } cid
            ? await _db.Companies.Where(c => c.Id == cid).Select(c => c.Name).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            : null;

        return Ok(new JobCardDetail(
            card.Id, card.Number, card.Date, companyName, customer?.Name, customer?.Code,
            card.ContactPerson, card.FaultDescription, card.Remarks, card.Technician, card.Status,
            card.Cost, card.Sell, card.CompletionRemarks, card.RowVersion, "new",
            [.. card.Lines.OrderBy(l => l.Sort).Select(l => new JobCardLineDetail(l.ItemId, l.Description, l.Serial))]));
    }

    private async Task<ActionResult<JobCardDetail>> LegacyJobCardDetail(
        long id, List<long> accessible, CancellationToken cancellationToken)
    {
        var accessibleText = accessible.Select(x => x.ToString(CultureInfo.InvariantCulture)).ToHashSet();

        var h = await _legacy.JobsMs
            .FirstOrDefaultAsync(x => x.Id == id && x.DataOrigin != "new", cancellationToken)
            .ConfigureAwait(false);

        if (h is null || !accessibleText.Contains(h.Company))
        {
            return NotFound();
        }

        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Code == h.Customer, cancellationToken).ConfigureAwait(false);
        var companyName = long.TryParse(h.Company, NumberStyles.Integer, CultureInfo.InvariantCulture, out var cid)
            ? await _db.Companies.Where(c => c.Id == cid).Select(c => c.Name).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            : null;

        // Legacy lines are the text blob — parse it back to (description, serial) for display.
        var lines = ParseItemsBlob(h.Items);
        var closed = string.Equals(h.Jstat, "CLOSED", StringComparison.OrdinalIgnoreCase);

        return Ok(new JobCardDetail(
            h.Id, h.Jobno, LegacyValue.Date(h.Jdate) ?? DateOnly.MinValue, companyName,
            customer?.Name ?? h.Customer, customer?.Code ?? h.Customer, h.Contactperson, h.FaultD, h.Remarks,
            h.Jobdoneby, h.Jstat, closed ? LegacyValue.Money(h.Cost) : null, closed ? LegacyValue.Money(h.Sell) : null,
            h.Completionremarks, 0, "legacy", lines));
    }

    /// <summary>Book in a job card — PENDING, with structured serial lines.</summary>
    [HttpPost]
    [RequirePermission(Permissions.JobCards)]
    public async Task<ActionResult<JobCardCreatedResponse>> Create(
        CreateJobCardRequest request, CancellationToken cancellationToken)
    {
        if (!_company.Accessible.Contains(request.CompanyId))
        {
            return Problem(statusCode: StatusCodes.Status403Forbidden, title: "You cannot raise a job card in that company.");
        }

        var created = await _creator.CreateAsync(
            new NewJobCard(
                request.CompanyId, request.CustomerId, request.Date, request.ContactPerson,
                request.FaultDescription, request.Remarks, request.Technician,
                [.. request.Lines.Select(l => new NewJobCardLine(l.ItemId, l.Description, l.Serial))]),
            cancellationToken).ConfigureAwait(false);

        return Ok(new JobCardCreatedResponse(created.Id, created.Number));
    }

    /// <summary>Close a job — the guarded PENDING → CLOSED transition; records cost, sell and completion.</summary>
    [HttpPost("{id:long}/close")]
    [RequirePermission(Permissions.JobCards)]
    [RequireChangeReason]
    public async Task<IActionResult> Close(long id, CloseJobCardRequest request, CancellationToken cancellationToken)
    {
        if (!await CallerMaySee(id, cancellationToken).ConfigureAwait(false))
        {
            return NotFound();
        }

        try
        {
            await _workflow.CloseAsync(
                id, new CloseJobCard(request.Cost, request.Sell, request.CompletionRemarks),
                request.ExpectedRowVersion, cancellationToken).ConfigureAwait(false);
            return NoContent();
        }
        catch (JobCardNotPendingException notPending)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: notPending.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "This job card was changed by someone else. Reload and try again.");
        }
    }

    private async Task<bool> CallerMaySee(long id, CancellationToken cancellationToken)
    {
        var accessible = _company.Accessible.ToList();
        var companyId = await _db.JobCards
            .IgnoreQueryFilters()
            .Where(j => j.Id == id && j.DeletedAt == null)
            .Select(j => j.CompanyId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return companyId is not null && accessible.Contains(companyId.Value);
    }

    /// <summary>Parses the legacy <c>items</c> blob (<c>Item : … | Qty : … | Serial No : …</c>) back to lines.</summary>
    private static List<JobCardLineDetail> ParseItemsBlob(string? blob)
    {
        var lines = new List<JobCardLineDetail>();
        if (string.IsNullOrWhiteSpace(blob)) return lines;

        foreach (var raw in blob.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string? description = null, serial = null;
            foreach (var part in raw.Split(" | ", StringSplitOptions.TrimEntries))
            {
                if (part.StartsWith("Item :", StringComparison.OrdinalIgnoreCase)) description = part[6..].Trim();
                else if (part.StartsWith("Serial No :", StringComparison.OrdinalIgnoreCase)) serial = part[11..].Trim();
            }
            lines.Add(new JobCardLineDetail(null, description, serial));
        }
        return lines;
    }
}
