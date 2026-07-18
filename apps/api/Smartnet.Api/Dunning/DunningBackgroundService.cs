using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Smartnet.Domain.Settings;
using Smartnet.Infrastructure.Persistence;

namespace Smartnet.Api.Dunning;

/// <summary>
/// Drains the dunning queue, one message at a time, off the request thread.
/// </summary>
/// <remarks>
/// For each job it loads the company's mail settings and the outstanding-bulk template, decrypts the
/// SMTP password (the one place allowed to, same protector as the settings screen), renders the body,
/// and sends — through <see cref="IMailSender"/>, which honours the <see cref="MailSettings.SendEnabled"/>
/// kill switch. That switch is the gate: off by default, so a message is <b>logged and dropped</b>
/// (status "blocked"), never sent, until the business turns sending on after the balances are
/// corrected. The <c>email_log</c> row moves queued → sent / failed / blocked; a failure is recorded,
/// not thrown, and never blocks the next customer.
/// </remarks>
public sealed partial class DunningBackgroundService : BackgroundService
{
    private readonly IDunningChannel _queue;
    private readonly IServiceScopeFactory _scopes;
    private readonly IDataProtectionProvider _protection;
    private readonly TimeProvider _time;
    private readonly ILogger<DunningBackgroundService> _logger;

    public DunningBackgroundService(
        IDunningChannel queue,
        IServiceScopeFactory scopes,
        IDataProtectionProvider protection,
        TimeProvider time,
        ILogger<DunningBackgroundService> logger)
    {
        _queue = queue;
        _scopes = scopes;
        _protection = protection;
        _time = time;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in _queue.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await ProcessAsync(job, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // One customer's failure never stops the queue (the legacy loop aborted on the first
                // throw and left the rest unsent). Logged with the id so it is diagnosable.
                LogJobFailed(_logger, job.EmailLogId, ex);
            }
        }
    }

    private async Task ProcessAsync(DunningJob job, CancellationToken cancellationToken)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SmartnetDbContext>();
        var mail = scope.ServiceProvider.GetRequiredService<IMailSender>();

        var log = await db.EmailLog
            .FirstOrDefaultAsync(e => e.Id == job.EmailLogId, cancellationToken)
            .ConfigureAwait(false);

        if (log is null)
        {
            return; // the row was removed; nothing to update
        }

        var settings = await db.MailSettings
            .FirstOrDefaultAsync(s => s.CompanyId == job.CompanyId, cancellationToken)
            .ConfigureAwait(false);

        log.SentAt = _time.GetUtcNow().UtcDateTime;

        if (settings is null)
        {
            log.Status = DunningStatus.Blocked;
            log.Error = "No mail server is configured for this company.";
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        var template = await db.EmailTemplates
            .FirstOrDefaultAsync(
                t => t.CompanyId == job.CompanyId && t.TemplateKey == EmailTemplateKeys.OutstandingBulk,
                cancellationToken)
            .ConfigureAwait(false);

        var password = string.IsNullOrEmpty(settings.PasswordEncrypted)
            ? null
            : _protection.CreateProtector("Smartnet.MailSettings.Password").Unprotect(settings.PasswordEncrypted);

        var (subject, body) = Render(template, job);

        var result = await mail
            .SendAsync(settings, password, [job.Recipient], subject, body, attachments: null, cancellationToken)
            .ConfigureAwait(false);

        // SendEnabled off → MailResult.Sent is false with the "switched off" message: that is the gate,
        // recorded as "blocked" rather than "failed" so the log distinguishes "we chose not to" from
        // "the server refused".
        log.Status = result.Sent
            ? DunningStatus.Sent
            : settings.SendEnabled ? DunningStatus.Failed : DunningStatus.Blocked;
        log.Error = result.Error;

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Renders the outstanding-bulk template, or a plain default when none is configured.</summary>
    private static (string Subject, string Body) Render(EmailTemplate? template, DunningJob job)
    {
        var total = job.Outstanding.ToString("#,##0.00", CultureInfo.InvariantCulture);

        if (template is not null)
        {
            return (Substitute(template.Subject, job, total), Substitute(template.Body, job, total));
        }

        var name = WebUtility.HtmlEncode(job.CustomerName);
        return (
            "Outstanding invoices",
            $"<p>Dear {name} Team,</p>"
            + $"<p>Our records show an outstanding balance of {total}. "
            + "Please arrange settlement at your earliest convenience.</p>"
            + "<p>Regards,<br/>Accounts</p>");
    }

    private static string Substitute(string text, DunningJob job, string total) => text
        .Replace("{{customer_name}}", job.CustomerName, StringComparison.Ordinal)
        .Replace("{{total}}", total, StringComparison.Ordinal)
        .Replace("{{outstanding}}", total, StringComparison.Ordinal);

    [LoggerMessage(EventId = 1, Level = LogLevel.Error, Message = "Dunning job for email_log {Id} failed unexpectedly")]
    private static partial void LogJobFailed(ILogger logger, long id, Exception exception);
}
