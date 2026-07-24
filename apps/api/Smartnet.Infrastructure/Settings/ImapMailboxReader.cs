using MailKit;
using MailKit.Net.Imap;
using MailKit.Security;
using MimeKit;
using Smartnet.Domain.Settings;

namespace Smartnet.Infrastructure.Settings;

/// <inheritdoc cref="IMailboxReader"/>
/// <remarks>
/// A fresh connection per call, closed at the end of it. IMAP is stateful and MailKit's client is not
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

    public async Task<IReadOnlyList<MailFolderInfo>> ListFoldersAsync(MailboxConnection connection, CancellationToken cancellationToken = default)
    {
        using var client = new ImapClient { Timeout = TimeoutMs };
        await ConnectAsync(client, connection, cancellationToken).ConfigureAwait(false);

        var folders = new List<MailFolderInfo>();

        foreach (var folder in await DiscoverAsync(client, cancellationToken).ConfigureAwait(false))
        {
            // \Noselect folders (pure containers) hold no messages and cannot be STATUSed.
            if (folder.Attributes.HasFlag(FolderAttributes.NonExistent) || folder.Attributes.HasFlag(FolderAttributes.NoSelect))
            {
                continue;
            }

            var unread = 0;
            try
            {
                await folder.StatusAsync(StatusItems.Unread, cancellationToken).ConfigureAwait(false);
                unread = folder.Unread;
            }
            catch (Exception ex) when (ex is ImapCommandException or ImapProtocolException)
            {
                // A folder that refuses STATUS still belongs in the list, just without a count.
            }

            var role = RoleOf(folder);
            folders.Add(new MailFolderInfo(folder.FullName, DisplayName(folder, role), role, unread));
        }

        await client.DisconnectAsync(quit: true, cancellationToken).ConfigureAwait(false);

        return folders
            .OrderBy(f => RoleOrder(f.Role))
            .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyList<MailHeader>> ListMessagesAsync(
        MailboxConnection connection,
        string folder,
        int skip,
        int take,
        CancellationToken cancellationToken = default)
    {
        using var client = new ImapClient { Timeout = TimeoutMs };
        await ConnectAsync(client, connection, cancellationToken).ConfigureAwait(false);

        var mailFolder = await OpenAsync(client, folder, FolderAccess.ReadOnly, cancellationToken).ConfigureAwait(false);

        // Indexes are 0-based, oldest first — so the newest page is the high end of the range.
        var total = mailFolder.Count;
        var end = total - 1 - skip;

        if (total == 0 || end < 0)
        {
            await client.DisconnectAsync(quit: true, cancellationToken).ConfigureAwait(false);
            return [];
        }

        var start = Math.Max(0, end - take + 1);

        var summaries = await mailFolder
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
        string folder,
        uint uid,
        bool markSeen,
        CancellationToken cancellationToken = default)
    {
        using var client = new ImapClient { Timeout = TimeoutMs };
        await ConnectAsync(client, connection, cancellationToken).ConfigureAwait(false);

        var mailFolder = await OpenAsync(client, folder, markSeen ? FolderAccess.ReadWrite : FolderAccess.ReadOnly, cancellationToken)
            .ConfigureAwait(false);

        var id = new UniqueId(uid);

        MimeMessage message;
        try
        {
            message = await mailFolder.GetMessageAsync(id, cancellationToken).ConfigureAwait(false);
        }
        catch (MessageNotFoundException)
        {
            // Deleted or expunged since the list was drawn — a gone message, not an error.
            await client.DisconnectAsync(quit: true, cancellationToken).ConfigureAwait(false);
            return null;
        }

        if (markSeen)
        {
            await mailFolder.AddFlagsAsync(id, MessageFlags.Seen, silent: true, cancellationToken).ConfigureAwait(false);
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

    public async Task SetSeenAsync(MailboxConnection connection, string folder, uint uid, bool seen, CancellationToken cancellationToken = default)
    {
        using var client = new ImapClient { Timeout = TimeoutMs };
        await ConnectAsync(client, connection, cancellationToken).ConfigureAwait(false);

        var mailFolder = await OpenAsync(client, folder, FolderAccess.ReadWrite, cancellationToken).ConfigureAwait(false);
        var id = new UniqueId(uid);

        if (seen)
        {
            await mailFolder.AddFlagsAsync(id, MessageFlags.Seen, silent: true, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await mailFolder.RemoveFlagsAsync(id, MessageFlags.Seen, silent: true, cancellationToken).ConfigureAwait(false);
        }

        await client.DisconnectAsync(quit: true, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(MailboxConnection connection, string folder, uint uid, CancellationToken cancellationToken = default)
    {
        using var client = new ImapClient { Timeout = TimeoutMs };
        await ConnectAsync(client, connection, cancellationToken).ConfigureAwait(false);

        var mailFolder = await OpenAsync(client, folder, FolderAccess.ReadWrite, cancellationToken).ConfigureAwait(false);
        var id = new UniqueId(uid);

        var trash = await SpecialAsync(client, MailFolderRole.Trash, cancellationToken).ConfigureAwait(false);

        // In Trash already (or no Trash exists) means gone for good; otherwise it is a move to Trash.
        if (RoleOf(mailFolder) == MailFolderRole.Trash || trash is null || trash.FullName == mailFolder.FullName)
        {
            await mailFolder.AddFlagsAsync(id, MessageFlags.Deleted, silent: true, cancellationToken).ConfigureAwait(false);
            await mailFolder.ExpungeAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await mailFolder.MoveToAsync(id, trash, cancellationToken).ConfigureAwait(false);
        }

        await client.DisconnectAsync(quit: true, cancellationToken).ConfigureAwait(false);
    }

    public async Task AppendToSentAsync(MailboxConnection connection, SentMessage message, CancellationToken cancellationToken = default)
    {
        using var client = new ImapClient { Timeout = TimeoutMs };
        await ConnectAsync(client, connection, cancellationToken).ConfigureAwait(false);

        var sent = await SpecialAsync(client, MailFolderRole.Sent, cancellationToken).ConfigureAwait(false);

        // No Sent folder to file it in — the message still went out over SMTP, so this is not an error.
        if (sent is not null)
        {
            var mime = new MimeMessage();
            mime.From.Add(new MailboxAddress(message.FromName, message.FromAddress));

            foreach (var recipient in message.To)
            {
                mime.To.Add(MailboxAddress.Parse(recipient));
            }

            mime.Subject = message.Subject;
            mime.Body = new TextPart("html") { Text = message.HtmlBody };
            mime.Date = DateTimeOffset.Now;

            await sent.AppendAsync(mime, MessageFlags.Seen, cancellationToken).ConfigureAwait(false);
        }

        await client.DisconnectAsync(quit: true, cancellationToken).ConfigureAwait(false);
    }

    // --- Connecting and opening ------------------------------------------------------------------

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

    private static async Task<IMailFolder> OpenAsync(ImapClient client, string folder, FolderAccess access, CancellationToken cancellationToken)
    {
        IMailFolder mailFolder;
        try
        {
            mailFolder = folder.Equals("INBOX", StringComparison.OrdinalIgnoreCase)
                ? client.Inbox
                : await client.GetFolderAsync(folder, cancellationToken).ConfigureAwait(false);
        }
        catch (FolderNotFoundException)
        {
            throw new MailboxReadException($"The folder '{folder}' does not exist in this mailbox.");
        }

        await mailFolder.OpenAsync(access, cancellationToken).ConfigureAwait(false);
        return mailFolder;
    }

    // --- Folder discovery and classification -----------------------------------------------------

    private static async Task<List<IMailFolder>> DiscoverAsync(ImapClient client, CancellationToken cancellationToken)
    {
        var folders = new List<IMailFolder> { client.Inbox };

        foreach (var ns in client.PersonalNamespaces)
        {
            var root = client.GetFolder(ns);

            foreach (var child in await root.GetSubfoldersAsync(subscribedOnly: false, cancellationToken).ConfigureAwait(false))
            {
                if (!child.FullName.Equals(client.Inbox.FullName, StringComparison.OrdinalIgnoreCase))
                {
                    folders.Add(child);
                }
            }
        }

        return folders;
    }

    /// <summary>The server's declared special folder for a role, or the first folder whose name says it is one.</summary>
    private static async Task<IMailFolder?> SpecialAsync(ImapClient client, MailFolderRole role, CancellationToken cancellationToken)
    {
        var special = role switch
        {
            MailFolderRole.Sent => SpecialFolder.Sent,
            MailFolderRole.Drafts => SpecialFolder.Drafts,
            MailFolderRole.Trash => SpecialFolder.Trash,
            MailFolderRole.Junk => SpecialFolder.Junk,
            MailFolderRole.Archive => SpecialFolder.Archive,
            _ => (SpecialFolder?)null,
        };

        if (special is not null && client.GetFolder(special.Value) is { } declared)
        {
            return declared;
        }

        foreach (var folder in await DiscoverAsync(client, cancellationToken).ConfigureAwait(false))
        {
            if (RoleOf(folder) == role)
            {
                return folder;
            }
        }

        return null;
    }

    private static MailFolderRole RoleOf(IMailFolder folder)
    {
        if (folder.Attributes.HasFlag(FolderAttributes.Inbox) || folder.FullName.Equals("INBOX", StringComparison.OrdinalIgnoreCase))
        {
            return MailFolderRole.Inbox;
        }

        if (folder.Attributes.HasFlag(FolderAttributes.Sent)) return MailFolderRole.Sent;
        if (folder.Attributes.HasFlag(FolderAttributes.Drafts)) return MailFolderRole.Drafts;
        if (folder.Attributes.HasFlag(FolderAttributes.Trash)) return MailFolderRole.Trash;
        if (folder.Attributes.HasFlag(FolderAttributes.Junk)) return MailFolderRole.Junk;
        if (folder.Attributes.HasFlag(FolderAttributes.Archive)) return MailFolderRole.Archive;

        // Servers that do not advertise SPECIAL-USE (older Dovecot, some cPanel setups) — fall back to names.
        return folder.Name.ToLowerInvariant() switch
        {
            "sent" or "sent items" or "sent mail" or "sent messages" => MailFolderRole.Sent,
            "drafts" or "draft" => MailFolderRole.Drafts,
            "trash" or "deleted" or "deleted items" or "deleted messages" => MailFolderRole.Trash,
            "junk" or "spam" or "junk email" or "junk e-mail" => MailFolderRole.Junk,
            "archive" or "archives" => MailFolderRole.Archive,
            _ => MailFolderRole.Other,
        };
    }

    private static string DisplayName(IMailFolder folder, MailFolderRole role) => role switch
    {
        MailFolderRole.Inbox => "Inbox",
        MailFolderRole.Sent => "Sent",
        MailFolderRole.Drafts => "Drafts",
        MailFolderRole.Trash => "Trash",
        MailFolderRole.Junk => "Junk",
        MailFolderRole.Archive => "Archive",
        _ => folder.Name,
    };

    private static int RoleOrder(MailFolderRole role) => role switch
    {
        MailFolderRole.Inbox => 0,
        MailFolderRole.Sent => 1,
        MailFolderRole.Drafts => 2,
        MailFolderRole.Junk => 3,
        MailFolderRole.Archive => 4,
        MailFolderRole.Trash => 5,
        _ => 6,
    };

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
