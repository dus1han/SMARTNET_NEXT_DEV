using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Smartnet.Api.Auth;
using Smartnet.Api.Contracts;
using Smartnet.Api.Dunning;
using Smartnet.Domain.Identity;
using Smartnet.Domain.Settings;
using Smartnet.Infrastructure.Entities;
using Smartnet.Infrastructure.Persistence;
using Smartnet.Infrastructure.Reporting;

namespace Smartnet.Api.Controllers;

/// <summary>
/// Bulk dunning — emailing customers their outstanding statements. The one write in Phase 4.
/// </summary>
/// <remarks>
/// This replaces the legacy <c>emailOSBulk</c>: the endpoint records one <c>email_log</c> row per
/// customer, enqueues, and returns at once; a background worker sends. Nothing blocks the request
/// thread, one slow recipient no longer hangs the run, and every message is accounted for in the log —
/// where the legacy app had no record at all.
///
/// <para><b>Gated by construction.</b> The actual send honours the per-company kill switch
/// (<see cref="MailSettings.SendEnabled"/>), which is <b>off by default</b>. So today this queues and
/// logs but sends nothing — deliberately: the balance figure it would email is the one Finding 1 shows
/// is wrong by Rs 1.55M, and emailing 223 customers a wrong balance is a business decision, not a code
/// one. The pipeline is built and proven; enabling it waits on the data-remediation decision.</para>
/// </remarks>
[ApiController]
[Route("api/dunning")]
public sealed class DunningController : ControllerBase
{
    private readonly SmartnetDbContext _db;
    private readonly SmartnetLegacyDbContext _legacy;
    private readonly ICompanyContext _company;
    private readonly IDunningChannel _queue;
    private readonly TimeProvider _time;

    public DunningController(
        SmartnetDbContext db,
        SmartnetLegacyDbContext legacy,
        ICompanyContext company,
        IDunningChannel queue,
        TimeProvider time)
    {
        _db = db;
        _legacy = legacy;
        _company = company;
        _queue = queue;
        _time = time;
    }

    /// <summary>Queues an outstanding statement to each selected customer (sending gated — see remarks).</summary>
    [HttpPost("outstanding")]
    [RequirePermission(Permissions.CustomerOutstanding)]
    public async Task<ActionResult<DunningResponse>> SendOutstanding(
        DunningRequest request,
        CancellationToken cancellationToken)
    {
        if (_company.Active is not { } companyId)
        {
            return Ok(new DunningResponse(0, 0, false, "No company in scope."));
        }

        var codes = request.Customers
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (codes.Count == 0)
        {
            return Ok(new DunningResponse(0, 0, false, "No customers selected."));
        }

        var companyText = companyId.ToString(CultureInfo.InvariantCulture);

        // Recompute each customer's outstanding here — never trust a client figure for a financial
        // email — summing only the positive balances, as the outstanding report does.
        var balances = await _legacy.InvoiceHs
            .Where(h => h.Company == companyText && codes.Contains(h.Customer!))
            .Select(h => new { h.Customer, h.Balance })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var outstanding = balances
            .GroupBy(h => h.Customer ?? string.Empty, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.Sum(x => LegacyValue.Money(x.Balance) is var b && b > 0m ? b : 0m),
                StringComparer.Ordinal);

        var customers = await _legacy.CusMs
            .Where(c => c.Cuscode != null && codes.Contains(c.Cuscode))
            .Select(c => new { c.Cuscode, c.Cusname, c.Email })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var settings = await _db.MailSettings
            .FirstOrDefaultAsync(s => s.CompanyId == companyId, cancellationToken)
            .ConfigureAwait(false);
        var sendEnabled = settings?.SendEnabled ?? false;

        var userId = long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid) ? uid : (long?)null;

        var pending = new List<(EmailLogEntry Log, string Code, string Name, string Email, decimal Outstanding)>();
        var skipped = 0;

        foreach (var c in customers)
        {
            var email = FirstEmail(c.Email);
            if (string.IsNullOrWhiteSpace(email))
            {
                skipped++; // no address on file — nothing to send
                continue;
            }

            var log = new EmailLogEntry
            {
                CompanyId = companyId,
                Recipient = email,
                TemplateKey = EmailTemplateKeys.OutstandingBulk,
                DocumentRef = $"OUTSTANDING:{c.Cuscode}",
                Status = DunningStatus.Queued,
                SentAt = _time.GetUtcNow().UtcDateTime,
                SentBy = userId,
            };

            _db.EmailLog.Add(log);
            pending.Add((log, c.Cuscode!, c.Cusname ?? string.Empty, email, outstanding.GetValueOrDefault(c.Cuscode!, 0m)));
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false); // populates the log ids

        foreach (var p in pending)
        {
            await _queue
                .EnqueueAsync(new DunningJob(p.Log.Id, companyId, p.Code, p.Name, p.Email, p.Outstanding), cancellationToken)
                .ConfigureAwait(false);
        }

        var message = sendEnabled
            ? $"{pending.Count} statement(s) queued — they will send in the background."
            : $"{pending.Count} statement(s) queued and logged, but sending is switched off, so nothing "
              + "will be sent. This is deliberate: the outstanding figure is known to be wrong (Finding 1). "
              + "Enable sending in Settings once the balances are corrected.";

        return Ok(new DunningResponse(pending.Count, skipped, sendEnabled, message));
    }

    /// <summary>The first address of a <c>;</c>-separated legacy email field.</summary>
    private static string? FirstEmail(string? raw) => raw
        ?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .FirstOrDefault();
}
