using MailKit;
using MailKit.Net.Imap;
using MailKit.Security;
using Smartnet.Domain.Settings;

namespace Smartnet.Infrastructure.Settings;

/// <inheritdoc cref="IMailboxReader"/>
/// <remarks>
/// A fresh connection per call, and closed at the end of it. IMAP is stateful and MailKit's client is not
/// thread-safe, so a shared long-lived one would need locking that would serialise every user's mail behind
/// every other's; for the handful of mailboxes a desk holds, connect-use-drop is simpler and fast enough.
/// </remarks>
public sealed class ImapMailboxReader : IMailboxReader
{
    // Enough for a slow server on a bad link; short enough that a dead host fails the request rather than
    // hanging the page. Applies to connect and to each command.
    private const int TimeoutMs = 20_000;

    public async Task<int> UnreadCountAsync(MailboxConnection connection, CancellationToken cancellationToken = default)
    {
        using var client = new ImapClient { Timeout = TimeoutMs };
        await ConnectAsync(client, connection, cancellationToken).ConfigureAwait(false);

        // STATUS, not an open — the badge does not need the messages, only the count.
        await client.Inbox.StatusAsync(StatusItems.Unread, cancellationToken).ConfigureAwait(false);
        var unread = client.Inbox.Unread;

        await client.DisconnectAsync(quit: true, cancellationToken).ConfigureAwait(false);
        return unread;
    }

    public async Task<IReadOnlyList<MailHeader>> ListInboxAsync(
        MailboxConnection connection,
        int skip,
        int take,
        CancellationToken cancellationToken = default)
    {
        using var client = new ImapClient { Timeout = TimeoutMs };
        await ConnectAsync(client, connection, cancellationToken).ConfigureAwait(false);

        var inbox = client.Inbox;
        await inbox.OpenAsync(FolderAccess.ReadOnly, cancellationToken).ConfigureAwait(false);

        // Indexes are 0-based, oldest first — so the newest page is the high end of the range.
        var total = inbox.Count;
        var end = total - 1 - skip;

        if (total == 0 || end < 0)
        {
            await client.DisconnectAsync(quit: true, cancellationToken).ConfigureAwait(false);
            return [];
        }

        var start = Math.Max(0, end - take + 1);

        var summaries = await inbox
            .FetchAsync(
                start,
                end,
                MessageSummaryItems.UniqueId
                    | MessageSummaryItems.Envelope
                    | MessageSummaryItems.Flags
                    | MessageSummaryItems.BodyStructure,
                cancellationToken)
            .ConfigureAwait(false);

        await client.DisconnectAsync(quit: true, cancellationToken).ConfigureAwait(false);

        // Fetch returns the range oldest-first; the newest belongs at the top of the list.
        return summaries
            .OrderByDescending(summary => summary.UniqueId.Id)
            .Select(ToHeader)
            .ToList();
    }

    public async Task<MailContent?> ReadMessageAsync(
        MailboxConnection connection,
        uint uid,
        bool markSeen,
        CancellationToken cancellationToken = default)
    {
        using var client = new ImapClient { Timeout = TimeoutMs };
        await ConnectAsync(client, connection, cancellationToken).ConfigureAwait(false);

        var inbox = client.Inbox;
        await inbox.OpenAsync(markSeen ? FolderAccess.ReadWrite : FolderAccess.ReadOnly, cancellationToken)
            .ConfigureAwait(false);

        var id = new UniqueId(uid);

        MimeKit.MimeMessage? message;
        try
        {
            message = await inbox.GetMessageAsync(id, cancellationToken).ConfigureAwait(false);
        }
        catch (MessageNotFoundException)
        {
            // Deleted or expunged since the list was drawn — a gone message, not an error.
            await client.DisconnectAsync(quit: true, cancellationToken).ConfigureAwait(false);
            return null;
        }

        if (markSeen)
        {
            await inbox.AddFlagsAsync(id, MessageFlags.Seen, silent: true, cancellationToken).ConfigureAwait(false);
        }

        await client.DisconnectAsync(quit: true, cancellationToken).ConfigureAwait(false);

        var html = message.HtmlBody;
        var from = message.From.Mailboxes.FirstOrDefault();

        return new MailContent(
            uid,
            from?.Name ?? string.Empty,
            from?.Address ?? string.Empty,
            string.Join(", ", message.To.Mailboxes.Select(m => m.Address)),
            string.IsNullOrWhiteSpace(message.Subject) ? "(no subject)" : message.Subject,
            message.Date,
            html ?? message.TextBody ?? string.Empty,
            IsHtml: html is not null);
    }

    private static async Task ConnectAsync(ImapClient client, MailboxConnection connection, CancellationToken cancellationToken)
    {
        try
        {
            // 993 is TLS from the first byte; 143 upgrades with STARTTLS. The setting picks which.
            var security = connection.UseSsl
                ? SecureSocketOptions.SslOnConnect
                : SecureSocketOptions.StartTlsWhenAvailable;

            await client.ConnectAsync(connection.Host, connection.Port, security, cancellationToken).ConfigureAwait(false);
            await client.AuthenticateAsync(connection.EmailAddress, connection.Password, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ImapCommandException or ImapProtocolException or AuthenticationException
                                      or IOException or System.Net.Sockets.SocketException)
        {
            // The wrong password, a firewalled port, a server that is down — all answerable by the user,
            // so reported rather than thrown as a 500 with a correlation id.
            throw new MailboxReadException($"Could not open {connection.EmailAddress}: {ex.Message}");
        }
    }

    private static MailHeader ToHeader(IMessageSummary summary)
    {
        var from = summary.Envelope?.From.Mailboxes.FirstOrDefault();

        return new MailHeader(
            summary.UniqueId.Id,
            from?.Name ?? string.Empty,
            from?.Address ?? string.Empty,
            string.IsNullOrWhiteSpace(summary.Envelope?.Subject) ? "(no subject)" : summary.Envelope!.Subject,
            summary.Envelope?.Date ?? summary.InternalDate ?? DateTimeOffset.MinValue,
            summary.Flags?.HasFlag(MessageFlags.Seen) ?? false,
            summary.Attachments.Any());
    }
}
