using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Smartnet.Api.Auth;
using Smartnet.Api.Contracts;
using Smartnet.Domain.Identity;
using Smartnet.Domain.Settings;
using Smartnet.Infrastructure.Persistence;

namespace Smartnet.Api.Controllers;

/// <summary>
/// The mailbox — the signed-in user working the mail assigned to them, read over IMAP and sent over SMTP.
/// </summary>
/// <remarks>
/// <para>
/// Gated by <c>email</c>, and every endpoint is scoped to <b>the caller's own</b> assigned mailboxes: the
/// mailbox id in the route is only honoured when a live <c>user_mail_accounts</c> row ties it to the signed-in
/// user, so holding <c>email</c> lets someone read their mail, not everyone's. The administration of the
/// mailboxes and the shared server lives elsewhere (<see cref="MailAccountsController"/>); this screen only
/// uses them.
/// </para>
/// <para>
/// The stored mailbox password is decrypted here, at the one place allowed to, and handed to the reader and
/// sender already in the clear — neither of them ever touches the database or the ciphertext.
/// </para>
/// </remarks>
[ApiController]
[Route("api/mail")]
[RequirePermission(Permissions.Email)]
public sealed class MailController : ControllerBase
{
    private readonly SmartnetDbContext _db;
    private readonly IMailboxReader _reader;
    private readonly IMailSender _sender;
    private readonly IDataProtector _passwords;

    public MailController(
        SmartnetDbContext db,
        IMailboxReader reader,
        IMailSender sender,
        IDataProtectionProvider protection)
    {
        _db = db;
        _reader = reader;
        _sender = sender;

        // The very same protector the mailbox password was written with — see MailAccountsController.
        _passwords = protection.CreateProtector(MailAccountsController.PasswordProtector);
    }

    private long CurrentUserId => long.Parse(
        User.FindFirstValue(ClaimTypes.NameIdentifier)!,
        CultureInfo.InvariantCulture);

    /// <summary>
    /// The mailboxes assigned to the signed-in user, each with its unread count.
    /// </summary>
    /// <remarks>
    /// The counts are read in parallel and each mailbox's failure is its own — a mailbox with a wrong
    /// password shows an error beside it rather than failing the whole switcher.
    /// </remarks>
    [HttpGet("mailboxes")]
    public async Task<ActionResult<IReadOnlyList<MailboxListItem>>> Mailboxes(CancellationToken cancellationToken)
    {
        var accounts = await AssignedMailboxesAsync(cancellationToken).ConfigureAwait(false);
        var server = await _db.MailServerSettings.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        var items = await Task.WhenAll(accounts.Select(async account =>
        {
            var connection = ConnectionFor(account, server);

            if (connection is null)
            {
                return new MailboxListItem(account.Id, account.DisplayName, account.EmailAddress, null,
                    "No password is set for this mailbox, or the incoming server is not configured.");
            }

            try
            {
                var unread = await _reader.UnreadCountAsync(connection, cancellationToken).ConfigureAwait(false);
                return new MailboxListItem(account.Id, account.DisplayName, account.EmailAddress, unread, null);
            }
            catch (MailboxReadException ex)
            {
                return new MailboxListItem(account.Id, account.DisplayName, account.EmailAddress, null, ex.Message);
            }
        })).ConfigureAwait(false);

        return Ok(items.ToList());
    }

    /// <summary>The folders of one assigned mailbox, each with its unread count.</summary>
    [HttpGet("{id:long}/folders")]
    public async Task<ActionResult<IReadOnlyList<MailFolderResponse>>> Folders(long id, CancellationToken cancellationToken)
    {
        var (connection, error) = await ConnectionOrErrorAsync(id, cancellationToken).ConfigureAwait(false);

        if (error is not null)
        {
            return error;
        }

        try
        {
            var folders = await _reader.ListFoldersAsync(connection!, cancellationToken).ConfigureAwait(false);
            return Ok(folders.Select(f => new MailFolderResponse(f.FullName, f.Name, f.Role.ToString(), f.Unread)).ToList());
        }
        catch (MailboxReadException ex)
        {
            return Problem(statusCode: StatusCodes.Status502BadGateway, title: "The mail server refused.", detail: ex.Message);
        }
    }

    /// <summary>One folder's messages, newest first, one page at a time.</summary>
    [HttpGet("{id:long}/messages")]
    public async Task<ActionResult<IReadOnlyList<MailHeaderResponse>>> Messages(
        long id,
        CancellationToken cancellationToken,
        string folder = "INBOX",
        int skip = 0,
        int take = 30)
    {
        var (connection, error) = await ConnectionOrErrorAsync(id, cancellationToken).ConfigureAwait(false);

        if (error is not null)
        {
            return error;
        }

        try
        {
            var headers = await _reader
                .ListMessagesAsync(connection!, folder, Math.Max(0, skip), Math.Clamp(take, 1, 100), cancellationToken)
                .ConfigureAwait(false);

            return Ok(headers.Select(ToResponse).ToList());
        }
        catch (MailboxReadException ex)
        {
            return Problem(statusCode: StatusCodes.Status502BadGateway, title: "The mail server refused.", detail: ex.Message);
        }
    }

    /// <summary>One message in full, marked read as it is opened.</summary>
    [HttpGet("{id:long}/messages/{uid:long}")]
    public async Task<ActionResult<MailMessageResponse>> Message(
        long id,
        long uid,
        CancellationToken cancellationToken,
        string folder = "INBOX")
    {
        var (connection, error) = await ConnectionOrErrorAsync(id, cancellationToken).ConfigureAwait(false);

        if (error is not null)
        {
            return error;
        }

        try
        {
            var content = await _reader
                .ReadMessageAsync(connection!, folder, (uint)uid, markSeen: true, cancellationToken)
                .ConfigureAwait(false);

            return content is null ? NotFound() : Ok(ToResponse(content));
        }
        catch (MailboxReadException ex)
        {
            return Problem(statusCode: StatusCodes.Status502BadGateway, title: "The mail server refused.", detail: ex.Message);
        }
    }

    /// <summary>Marks a message read or unread.</summary>
    [HttpPost("{id:long}/messages/{uid:long}/seen")]
    public async Task<IActionResult> SetSeen(
        long id,
        long uid,
        CancellationToken cancellationToken,
        string folder = "INBOX",
        bool seen = true)
    {
        var (connection, error) = await ConnectionOrErrorAsync(id, cancellationToken).ConfigureAwait(false);

        if (error is not null)
        {
            return error;
        }

        try
        {
            await _reader.SetSeenAsync(connection!, folder, (uint)uid, seen, cancellationToken).ConfigureAwait(false);
            return NoContent();
        }
        catch (MailboxReadException ex)
        {
            return Problem(statusCode: StatusCodes.Status502BadGateway, title: "The mail server refused.", detail: ex.Message);
        }
    }

    /// <summary>Deletes a message — to Trash, or for good if it is already there.</summary>
    [HttpDelete("{id:long}/messages/{uid:long}")]
    public async Task<IActionResult> Delete(
        long id,
        long uid,
        CancellationToken cancellationToken,
        string folder = "INBOX")
    {
        var (connection, error) = await ConnectionOrErrorAsync(id, cancellationToken).ConfigureAwait(false);

        if (error is not null)
        {
            return error;
        }

        try
        {
            await _reader.DeleteAsync(connection!, folder, (uint)uid, cancellationToken).ConfigureAwait(false);
            return NoContent();
        }
        catch (MailboxReadException ex)
        {
            return Problem(statusCode: StatusCodes.Status502BadGateway, title: "The mail server refused.", detail: ex.Message);
        }
    }

    /// <summary>Sends a message as one of the caller's mailboxes — compose or reply.</summary>
    [HttpPost("{id:long}/send")]
    public async Task<IActionResult> Send(long id, SendMailRequest request, CancellationToken cancellationToken)
    {
        var (account, server, error) = await ResolveAsync(id, cancellationToken).ConfigureAwait(false);

        if (error is not null)
        {
            return error;
        }

        if (server is null)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "The mail server is not configured yet.");
        }

        var recipients = SplitAddresses(request.To);

        if (recipients.Count == 0)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Enter at least one recipient.");
        }

        var password = string.IsNullOrEmpty(account!.PasswordEncrypted)
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
            // The mailbox is the sender; there is no company kill switch on a person's own outbound mail.
            SendEnabled = true,
        };

        // The body is typed as plain text — encode it and keep the line breaks the writer put in.
        var html = System.Net.WebUtility.HtmlEncode(request.Body ?? string.Empty).Replace("\n", "<br>", StringComparison.Ordinal);

        var result = await _sender
            .SendAsync(settings, password, recipients, request.Subject ?? string.Empty, html, attachments: null, cancellationToken)
            .ConfigureAwait(false);

        if (!result.Sent)
        {
            return Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "The mail server rejected the message.",
                detail: result.Error);
        }

        // File a copy in Sent, so sending leaves a record where the user expects one. Best-effort: the
        // message has already gone, so a mailbox that will not take the copy must not turn a sent message
        // into a failed one.
        var connection = ConnectionFor(account, server);

        if (connection is not null)
        {
            try
            {
                await _reader
                    .AppendToSentAsync(
                        connection,
                        new SentMessage(account.DisplayName, account.EmailAddress, recipients, request.Subject ?? string.Empty, html),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (MailboxReadException)
            {
                // No Sent folder, or the append was refused — the mail still went out.
            }
        }

        return NoContent();
    }

    // --- Helpers ---------------------------------------------------------------------------------

    /// <summary>
    /// Resolves the route mailbox to a ready IMAP/SMTP connection, or the error to return — the caller's own
    /// mailbox with a password and a configured server, or a 404/400 saying which of those is missing.
    /// </summary>
    private async Task<(MailboxConnection? Connection, ObjectResult? Error)> ConnectionOrErrorAsync(
        long id,
        CancellationToken cancellationToken)
    {
        var (account, server, error) = await ResolveAsync(id, cancellationToken).ConfigureAwait(false);

        if (error is not null)
        {
            return (null, error);
        }

        var connection = ConnectionFor(account!, server);

        if (connection is null)
        {
            return (null, (ObjectResult)Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "This mailbox has no password set, or the incoming server is not configured."));
        }

        return (connection, null);
    }

    /// <summary>The signed-in user's enabled, assigned mailboxes — the switcher's list.</summary>
    private Task<List<MailAccount>> AssignedMailboxesAsync(CancellationToken cancellationToken) => _db.UserMailAccounts
        .Where(link => link.UserId == CurrentUserId)
        .Join(_db.MailAccounts.Where(m => m.Enabled), link => link.MailAccountId, account => account.Id, (_, account) => account)
        .OrderBy(account => account.EmailAddress)
        .ToListAsync(cancellationToken);

    /// <summary>
    /// The mailbox for a route id, but only if it is the caller's — the join to <c>user_mail_accounts</c> is
    /// the authorisation, so a stranger's mailbox id resolves to "not found" rather than to their mail.
    /// </summary>
    private async Task<(MailAccount? Account, MailServerSettings? Server, ObjectResult? Error)> ResolveAsync(
        long id,
        CancellationToken cancellationToken)
    {
        var account = await _db.UserMailAccounts
            .Where(link => link.UserId == CurrentUserId && link.MailAccountId == id)
            .Join(_db.MailAccounts, link => link.MailAccountId, m => m.Id, (_, m) => m)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (account is null)
        {
            return (null, null, (ObjectResult)Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "That mailbox is not one of yours."));
        }

        var server = await _db.MailServerSettings.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return (account, server, null);
    }

    private MailboxConnection? ConnectionFor(MailAccount account, MailServerSettings? server)
    {
        if (server is null
            || string.IsNullOrWhiteSpace(server.IncomingHost)
            || string.IsNullOrEmpty(account.PasswordEncrypted))
        {
            return null;
        }

        return new MailboxConnection(
            server.IncomingHost,
            server.IncomingPort,
            server.IncomingUseSsl,
            account.EmailAddress,
            _passwords.Unprotect(account.PasswordEncrypted));
    }

    private static List<string> SplitAddresses(string raw) => raw
        .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .ToList();

    private static MailHeaderResponse ToResponse(MailHeader h) =>
        new(h.Uid, h.FromName, h.FromAddress, h.Subject, h.Date, h.Seen, h.HasAttachments);

    private static MailMessageResponse ToResponse(MailContent c) =>
        new(c.Uid, c.FromName, c.FromAddress, c.To, c.Subject, c.Date, c.Body, c.IsHtml);
}
