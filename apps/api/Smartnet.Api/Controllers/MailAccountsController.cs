using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Smartnet.Api.Auditing;
using Smartnet.Api.Auth;
using Smartnet.Api.Contracts;
using Smartnet.Domain.Identity;
using Smartnet.Domain.Settings;
using Smartnet.Infrastructure.Persistence;
using Smartnet.Infrastructure.Settings;

namespace Smartnet.Api.Controllers;

/// <summary>
/// The shared mail server + cPanel connection (Dev-Admin), and the mailboxes on it (settings.manage).
/// </summary>
/// <remarks>
/// <para>
/// The server and the cPanel API token are the Administration → cPanel screen, restricted to Dev-Admin — the
/// token can create and re-password real mailboxes on the host. The mailboxes themselves are the Mail
/// accounts screen. When cPanel provisioning is on, adding an account here (or changing its password) is
/// pushed to the host so it appears in cPanel and Roundcube; removing one never touches the real mailbox.
/// </para>
/// <para>
/// <b>Secrets are write-only.</b> A GET returns only whether a password or token is set; a save with a null
/// value leaves the stored one alone. Encrypted at rest, redacted in the audit log.
/// </para>
/// </remarks>
[ApiController]
[Route("api/mail-accounts")]
[RequirePermission(Permissions.MailAccounts)]
public sealed class MailAccountsController : ControllerBase
{
    public const string PasswordProtector = "Smartnet.MailAccount.Password";
    public const string CpanelTokenProtector = "Smartnet.Cpanel.ApiToken";

    private readonly SmartnetDbContext _db;
    private readonly IMailSender _mail;
    private readonly ICpanelMailProvisioner _cpanel;
    private readonly IDataProtector _passwords;
    private readonly IDataProtector _tokens;

    public MailAccountsController(
        SmartnetDbContext db,
        IMailSender mail,
        ICpanelMailProvisioner cpanel,
        IDataProtectionProvider protection)
    {
        _db = db;
        _mail = mail;
        _cpanel = cpanel;
        _passwords = protection.CreateProtector(PasswordProtector);
        _tokens = protection.CreateProtector(CpanelTokenProtector);
    }

    // --- The shared server + cPanel (Dev-Admin) --------------------------------------------------

    /// <summary>The shared mail server and cPanel connection, or sensible defaults when none is saved.</summary>
    [HttpGet("server-settings")]
    [RequirePermission(Permissions.SystemDevAdmin)]
    public async Task<ActionResult<MailServerSettingsResponse>> ServerSettings(CancellationToken cancellationToken)
    {
        var s = await _db.MailServerSettings.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        return Ok(new MailServerSettingsResponse(
            s?.MailDomain,
            s?.OutgoingHost ?? string.Empty,
            s?.OutgoingPort ?? 587,
            s?.OutgoingUseSsl ?? true,
            s?.IncomingProtocol ?? IncomingMailProtocols.Imap,
            s?.IncomingHost ?? string.Empty,
            s?.IncomingPort ?? 993,
            s?.IncomingUseSsl ?? true,
            s?.CpanelHost,
            s?.CpanelPort ?? 2083,
            s?.CpanelUsername,
            HasCpanelApiToken: !string.IsNullOrEmpty(s?.CpanelApiTokenEncrypted)));
    }

    /// <summary>Saves the shared mail server and cPanel connection — one row, created on first save.</summary>
    [HttpPut("server-settings")]
    [RequirePermission(Permissions.SystemDevAdmin)]
    [RequireChangeReason]
    public async Task<IActionResult> SaveServerSettings(
        SaveMailServerSettingsRequest request,
        CancellationToken cancellationToken)
    {
        var s = await _db.MailServerSettings.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        if (s is null)
        {
            s = new MailServerSettings();
            _db.MailServerSettings.Add(s);
        }

        s.MailDomain = request.MailDomain?.Trim();
        s.OutgoingHost = request.OutgoingHost.Trim();
        s.OutgoingPort = request.OutgoingPort;
        s.OutgoingUseSsl = request.OutgoingUseSsl;
        s.IncomingProtocol = request.IncomingProtocol;
        s.IncomingHost = request.IncomingHost.Trim();
        s.IncomingPort = request.IncomingPort;
        s.IncomingUseSsl = request.IncomingUseSsl;

        s.CpanelHost = request.CpanelHost?.Trim();
        s.CpanelPort = request.CpanelPort;
        s.CpanelUsername = request.CpanelUsername?.Trim();

        // Null token leaves the stored one alone — what the form sends when it is not retyped.
        if (!string.IsNullOrEmpty(request.CpanelApiToken))
        {
            s.CpanelApiTokenEncrypted = _tokens.Protect(request.CpanelApiToken);
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return NoContent();
    }

    // --- Mailboxes (settings.manage) -------------------------------------------------------------

    /// <summary>The mail domain, for the add screen's fixed suffix — no cPanel-screen permission needed.</summary>
    [HttpGet("domain")]
    public async Task<ActionResult<MailDomainResponse>> Domain(CancellationToken cancellationToken)
    {
        var domain = await _db.MailServerSettings
            .Select(s => s.MailDomain)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return Ok(new MailDomainResponse(domain));
    }

    /// <summary>Every mail account, newest first.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<MailAccountResponse>>> List(CancellationToken cancellationToken)
    {
        var accounts = await _db.MailAccounts
            .OrderByDescending(a => a.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Ok(accounts.Select(ToResponse).ToList());
    }

    /// <summary>
    /// Adds a mail account. With cPanel provisioning on, the mailbox is created on the host first — and if
    /// the host refuses, nothing is saved here, so the two never disagree.
    /// </summary>
    [HttpPost]
    [RequireChangeReason]
    public async Task<ActionResult<MailAccountResponse>> Create(
        SaveMailAccountRequest request,
        CancellationToken cancellationToken)
    {
        var email = request.EmailAddress.Trim();

        // One address, one account. Without this the same mailbox could be added twice — and with cPanel on,
        // the second add hits the host's own "already exists" as a raw 502 that reads like a fault. The
        // column collation is case-insensitive, so Sales@ and sales@ are the same mailbox here too. (The two
        // pre-existing mailboxes are being inserted straight into the table; this is what stops a re-add of
        // them, or of anything, from the screen.)
        var taken = await _db.MailAccounts
            .AnyAsync(a => a.EmailAddress == email, cancellationToken)
            .ConfigureAwait(false);

        if (taken)
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: $"{email} is already a mail account.");
        }

        var server = await _db.MailServerSettings.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        var cpanel = CpanelCredentialsOrNull(server);

        if (cpanel is not null)
        {
            if (string.IsNullOrEmpty(request.Password))
            {
                return Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "A password is required to create the mailbox on cPanel.");
            }

            var failed = await ProvisionAsync(
                () => _cpanel.CreateMailboxAsync(cpanel, request.EmailAddress.Trim(), request.Password!, cancellationToken),
                "create the mailbox").ConfigureAwait(false);

            if (failed is not null)
            {
                return failed;
            }
        }

        var account = new MailAccount();
        Apply(account, request);

        _db.MailAccounts.Add(account);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Created($"/api/mail-accounts/{account.Id}", ToResponse(account));
    }

    /// <summary>Updates a mail account. A new password is pushed to cPanel too when provisioning is on.</summary>
    [HttpPut("{id:long}")]
    [RequireChangeReason]
    public async Task<IActionResult> Update(
        long id,
        SaveMailAccountRequest request,
        CancellationToken cancellationToken)
    {
        var account = await _db.MailAccounts
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (account is null)
        {
            return NotFound();
        }

        var server = await _db.MailServerSettings.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        var cpanel = CpanelCredentialsOrNull(server);

        if (!string.IsNullOrEmpty(request.Password) && cpanel is not null)
        {
            var failed = await ProvisionAsync(
                () => _cpanel.SetPasswordAsync(cpanel, request.EmailAddress.Trim(), request.Password!, cancellationToken),
                "change the mailbox password").ConfigureAwait(false);

            if (failed is not null)
            {
                return failed;
            }
        }

        Apply(account, request);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return NoContent();
    }

    /// <summary>
    /// Removes a mail account. With cPanel configured, the real mailbox is deleted on the host too (and its
    /// stored mail with it) — and if the host refuses, nothing is removed here either, so the two stay in step.
    /// </summary>
    [HttpDelete("{id:long}")]
    [RequireChangeReason]
    public async Task<IActionResult> Delete(long id, CancellationToken cancellationToken)
    {
        var account = await _db.MailAccounts
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (account is null)
        {
            return NotFound();
        }

        var server = await _db.MailServerSettings.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        var cpanel = CpanelCredentialsOrNull(server);

        if (cpanel is not null)
        {
            var failed = await ProvisionAsync(
                () => _cpanel.DeleteMailboxAsync(cpanel, account.EmailAddress, cancellationToken),
                "delete the mailbox").ConfigureAwait(false);

            if (failed is not null)
            {
                return failed;
            }
        }

        _db.MailAccounts.Remove(account);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return NoContent();
    }

    /// <summary>Sends a test message through this account over the shared server.</summary>
    [HttpPost("{id:long}/test")]
    public async Task<IActionResult> SendTest(
        long id,
        SendTestEmailRequest request,
        CancellationToken cancellationToken)
    {
        var account = await _db.MailAccounts
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (account is null)
        {
            return NotFound();
        }

        var server = await _db.MailServerSettings.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        if (server is null)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "The mail server is not configured yet. Set it on the cPanel screen first.");
        }

        var password = string.IsNullOrEmpty(account.PasswordEncrypted)
            ? null
            : _passwords.Unprotect(account.PasswordEncrypted);

        var settings = new MailSettings
        {
            Host = server.OutgoingHost,
            Port = server.OutgoingPort,
            UseSsl = server.OutgoingUseSsl,
            Username = account.EmailAddress,
            FromAddress = account.EmailAddress,
            FromName = account.DisplayName,
            SendEnabled = true,
        };

        var result = await _mail
            .SendTestAsync(settings, password, request.To, cancellationToken)
            .ConfigureAwait(false);

        if (!result.Sent)
        {
            return Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "The mail server rejected the message.",
                detail: result.Error);
        }

        return NoContent();
    }

    // --- Helpers ---------------------------------------------------------------------------------

    /// <summary>
    /// The cPanel credentials when the connection is fully configured — host, username <i>and</i> token —
    /// or null when it is not. A configured connection is an active one: there is no separate on/off switch,
    /// so this is what decides whether a create or a password change is pushed to the host.
    /// </summary>
    private CpanelCredentials? CpanelCredentialsOrNull(MailServerSettings? server)
    {
        if (server is null
            || string.IsNullOrWhiteSpace(server.CpanelHost)
            || string.IsNullOrWhiteSpace(server.CpanelUsername)
            || string.IsNullOrEmpty(server.CpanelApiTokenEncrypted))
        {
            return null;
        }

        return new CpanelCredentials(
            server.CpanelHost,
            server.CpanelPort,
            server.CpanelUsername,
            _tokens.Unprotect(server.CpanelApiTokenEncrypted));
    }

    /// <summary>
    /// Runs a cPanel operation, translating a refusal into the response to return — or null when it succeeded
    /// and the caller may go on to save.
    /// </summary>
    private async Task<ActionResult?> ProvisionAsync(Func<Task> operation, string what)
    {
        try
        {
            await operation().ConfigureAwait(false);
            return null;
        }
        catch (CpanelProvisioningException ex)
        {
            return Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: $"cPanel could not {what}.",
                detail: ex.Message);
        }
    }

    private void Apply(MailAccount account, SaveMailAccountRequest request)
    {
        account.DisplayName = request.DisplayName.Trim();
        account.EmailAddress = request.EmailAddress.Trim();
        account.Enabled = request.Enabled;

        if (!string.IsNullOrEmpty(request.Password))
        {
            account.PasswordEncrypted = _passwords.Protect(request.Password);
        }
    }

    private static MailAccountResponse ToResponse(MailAccount a) => new(
        a.Id,
        a.DisplayName,
        a.EmailAddress,
        HasPassword: !string.IsNullOrEmpty(a.PasswordEncrypted),
        a.Enabled);
}
