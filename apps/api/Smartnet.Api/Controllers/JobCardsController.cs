using System.Globalization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Smartnet.Api.Auditing;
using Smartnet.Api.Auth;
using Smartnet.Api.Contracts;
using Smartnet.Domain.Auditing;
using Smartnet.Domain.Documents;
using Smartnet.Domain.Identity;
using Smartnet.Domain.MasterData;
using Smartnet.Domain.Reporting;
using Smartnet.Domain.Settings;
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
    private readonly IJobSheetRenderer _jobSheet;
    private readonly IAuditWriter _audit;
    private readonly IMailSender _mail;
    private readonly IDataProtectionProvider _protection;

    public JobCardsController(
        IJobCardCreator creator,
        IJobCardWorkflow workflow,
        ICompanyContext company,
        SmartnetDbContext db,
        SmartnetLegacyDbContext legacy,
        IJobSheetRenderer jobSheet,
        IAuditWriter audit,
        IMailSender mail,
        IDataProtectionProvider protection)
    {
        _creator = creator;
        _workflow = workflow;
        _company = company;
        _db = db;
        _legacy = legacy;
        _jobSheet = jobSheet;
        _audit = audit;
        _mail = mail;
        _protection = protection;
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

    /// <summary>The job sheet for this card as a downloadable PDF, rendered in its company's own layout.</summary>
    [HttpGet("{id:long}/pdf")]
    [RequirePermission(Permissions.JobCards)]
    public async Task<IActionResult> Pdf(long id, CancellationToken cancellationToken)
    {
        // Guard visibility by the job's company (jobs_m holds both this app's and legacy cards) before rendering.
        var meta = await _legacy.JobsMs
            .Where(j => j.Id == id)
            .Select(j => new { j.Company, j.Jobno })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (meta is null
            || !long.TryParse(meta.Company, NumberStyles.Integer, CultureInfo.InvariantCulture, out var companyId)
            || !_company.Accessible.Contains(companyId))
        {
            return NotFound();
        }

        var pdf = await _jobSheet.RenderAsync(id, cancellationToken).ConfigureAwait(false);

        if (pdf is null)
        {
            return NotFound();
        }

        // "Was this sheet ever given to the customer?" is a question the History tab has to answer, and
        // a download is how it leaves the building on paper. Recorded like the send, not like a read.
        await _audit.RecordAsync(
            AuditAction.Print,
            nameof(JobCard),
            id.ToString(CultureInfo.InvariantCulture),
            details: new { jobNo = meta.Jobno, document = "job-sheet" },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return File(pdf, "application/pdf", $"job-sheet-{meta.Jobno}.pdf");
    }

    /// <summary>
    /// Who this job sheet can be emailed to — the customer's saved contacts that have an address — plus
    /// the message that would be sent, so the dialog can show it before anything leaves.
    /// </summary>
    [HttpGet("{id:long}/recipients")]
    [RequirePermission(Permissions.JobCards)]
    public async Task<ActionResult<JobSheetRecipients>> Recipients(long id, CancellationToken cancellationToken)
    {
        var job = await VisibleJobAsync(id, cancellationToken).ConfigureAwait(false);

        if (job is null)
        {
            return NotFound();
        }

        var contacts = await CustomerContactsAsync(job.Customer, cancellationToken).ConfigureAwait(false);
        var (subject, body) = JobSheetMessage(job.Jobno, await CompanyNameAsync(job.CompanyId, cancellationToken).ConfigureAwait(false));

        return Ok(new JobSheetRecipients(
            contacts,
            subject,
            body,
            $"job-sheet-{job.Jobno}.pdf",
            await SendBlockedReasonAsync(job.CompanyId, contacts, cancellationToken).ConfigureAwait(false)));
    }

    /// <summary>Emails the job sheet, as a PDF attachment, to the chosen saved contacts.</summary>
    [HttpPost("{id:long}/email")]
    [RequirePermission(Permissions.JobCards)]
    public async Task<ActionResult<EmailDocumentResponse>> Email(
        long id,
        EmailDocumentRequest request,
        CancellationToken cancellationToken)
    {
        var job = await VisibleJobAsync(id, cancellationToken).ConfigureAwait(false);

        if (job is null)
        {
            return NotFound();
        }

        // Re-resolve from the customer's own contacts rather than trusting the posted ids: otherwise the
        // endpoint would mail this customer's job sheet to any contact id the caller cared to name.
        var offered = await CustomerContactsAsync(job.Customer, cancellationToken).ConfigureAwait(false);
        var chosen = offered.Where(c => request.ContactIds.Contains(c.Id)).ToList();

        if (chosen.Count == 0)
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "None of the chosen contacts belong to this job card's customer.",
            });
        }

        var settings = await _db.MailSettings
            .FirstOrDefaultAsync(s => s.CompanyId == job.CompanyId, cancellationToken)
            .ConfigureAwait(false);

        if (settings is null)
        {
            return Ok(new EmailDocumentResponse(false, [], "No mail server is configured for this company."));
        }

        var pdf = await _jobSheet.RenderAsync(id, cancellationToken).ConfigureAwait(false);

        if (pdf is null)
        {
            return NotFound();
        }

        var password = string.IsNullOrEmpty(settings.PasswordEncrypted)
            ? null
            : _protection.CreateProtector("Smartnet.MailSettings.Password").Unprotect(settings.PasswordEncrypted);

        var (subject, body) = JobSheetMessage(job.Jobno, await CompanyNameAsync(job.CompanyId, cancellationToken).ConfigureAwait(false));
        var recipients = chosen.Select(c => c.Email).ToList();

        var result = await _mail.SendAsync(
            settings,
            password,
            recipients,
            subject,
            body,
            [new MailAttachment($"job-sheet-{job.Jobno}.pdf", "application/pdf", pdf)],
            cancellationToken).ConfigureAwait(false);

        // Recorded either way. A send that the server refused is exactly the event someone goes looking
        // for when the customer says they never received it — "we tried and it bounced" is an answer.
        await _audit.RecordAsync(
            AuditAction.Email,
            nameof(JobCard),
            id.ToString(CultureInfo.InvariantCulture),
            details: new
            {
                jobNo = job.Jobno,
                document = "job-sheet",
                to = recipients,
                sent = result.Sent,
                error = result.Error,
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return Ok(new EmailDocumentResponse(result.Sent, recipients, result.Error));
    }

    /// <summary>The job row, only if the caller's companies include the one that owns it.</summary>
    private async Task<VisibleJob?> VisibleJobAsync(long id, CancellationToken cancellationToken)
    {
        var meta = await _legacy.JobsMs
            .Where(j => j.Id == id)
            .Select(j => new { j.Company, j.Jobno, j.Customer })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (meta is null
            || !long.TryParse(meta.Company, NumberStyles.Integer, CultureInfo.InvariantCulture, out var companyId)
            || !_company.Accessible.Contains(companyId))
        {
            return null;
        }

        return new VisibleJob(companyId, meta.Jobno ?? string.Empty, meta.Customer);
    }

    /// <summary>
    /// The customer's saved contacts that can actually receive mail. Document contacts are ticked by
    /// default — they are who the sheet would have been handed to — and notifications-only ones are
    /// offered unticked rather than assumed.
    /// </summary>
    private async Task<List<DocumentContact>> CustomerContactsAsync(string? customerCode, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(customerCode))
        {
            return [];
        }

        return await _db.CustomerContacts
            .Where(c => c.Customer.Code == customerCode && c.Email != null && c.Email != "")
            .OrderBy(c => c.Name)
            .Select(c => new DocumentContact(
                c.Id,
                c.Name,
                c.Email!,
                c.Usage,
                c.Usage == ContactUsage.DocumentsAndNotifications))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<string> CompanyNameAsync(long companyId, CancellationToken cancellationToken) =>
        await _db.Companies
            .Where(c => c.Id == companyId)
            .Select(c => c.Name)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false) ?? "SMARTNET";

    /// <summary>
    /// Why Send would fail, decided before the user picks anybody — an unconfigured server or the
    /// company's send kill switch. Null when the send would genuinely be attempted.
    /// </summary>
    private async Task<string?> SendBlockedReasonAsync(
        long companyId,
        List<DocumentContact> contacts,
        CancellationToken cancellationToken)
    {
        if (contacts.Count == 0)
        {
            return "This customer has no contact with an email address. Add one on the customer first.";
        }

        var settings = await _db.MailSettings
            .Where(s => s.CompanyId == companyId)
            .Select(s => new { s.Host, s.SendEnabled, s.FromAddress, s.Username })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (settings is null || string.IsNullOrWhiteSpace(settings.Host))
        {
            return "No mail server is configured for this company. Set one in Settings.";
        }

        if (string.IsNullOrWhiteSpace(settings.FromAddress) && string.IsNullOrWhiteSpace(settings.Username))
        {
            return "No from-address is configured for this company. Set one in Settings.";
        }

        // The kill switch, reported rather than discovered: MailSender refuses the send when this is off.
        return settings.SendEnabled
            ? null
            : "Sending is switched off for this company. Turn it on in Settings to send.";
    }

    /// <summary>
    /// The job-sheet message. Fixed, not a configurable template: it says one thing, and a covering note
    /// whose wording drifts per send is a covering note nobody can vouch for.
    /// </summary>
    private static (string Subject, string Body) JobSheetMessage(string jobNo, string companyName)
    {
        var subject = $"Job sheet {jobNo} — {companyName}";

        var body =
            $"""
             <p>Dear Customer,</p>
             <p>Please find attached the job sheet for <strong>{jobNo}</strong>.</p>
             <p>Kindly present this job sheet when collecting the equipment.</p>
             <p>Thank you,<br />{companyName}</p>
             """;

        return (subject, body);
    }

    /// <summary>A job the caller may see: its company, its number and its customer code.</summary>
    private sealed record VisibleJob(long CompanyId, string Jobno, string? Customer);

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

        // The real row_version off the adopted row, so a legacy card can be closed (the close is guarded by
        // an optimistic-concurrency check that a hardcoded 0 would always fail).
        var rowVersion = await _db.JobCards
            .IgnoreQueryFilters()
            .Where(j => j.Id == h.Id)
            .Select(j => j.RowVersion)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return Ok(new JobCardDetail(
            h.Id, h.Jobno, LegacyValue.Date(h.Jdate) ?? DateOnly.MinValue, companyName,
            customer?.Name ?? h.Customer, customer?.Code ?? h.Customer, h.Contactperson, h.FaultD, h.Remarks,
            h.Jobdoneby, h.Jstat, closed ? LegacyValue.Money(h.Cost) : null, closed ? LegacyValue.Money(h.Sell) : null,
            h.Completionremarks, rowVersion, "legacy", lines));
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

    /// <summary>
    /// Close a job — the guarded PENDING → CLOSED transition; records cost, sell and completion. Closing a
    /// job means it is completed, so it needs no change reason (the legacy app asked for none either).
    /// </summary>
    [HttpPost("{id:long}/close")]
    [RequirePermission(Permissions.JobCards)]
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
