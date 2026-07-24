namespace Smartnet.Domain.Settings;

/// <summary>
/// How to reach one IMAP mailbox — the shared incoming server, and the mailbox's own login, already
/// decrypted. Decryption happens once, at the controller allowed to do it; the reader never sees ciphertext
/// and never touches the database, exactly like <see cref="IMailSender"/>.
/// </summary>
public sealed record MailboxConnection(string Host, int Port, bool UseSsl, string EmailAddress, string Password);

/// <summary>A message as it appears in the inbox list — envelope and flags only, no body fetched.</summary>
public sealed record MailHeader(
    uint Uid,
    string FromName,
    string FromAddress,
    string Subject,
    DateTimeOffset Date,
    bool Seen,
    bool HasAttachments);

/// <summary>A single message opened for reading. <see cref="IsHtml"/> says how to render <see cref="Body"/>.</summary>
public sealed record MailContent(
    uint Uid,
    string FromName,
    string FromAddress,
    string To,
    string Subject,
    DateTimeOffset Date,
    string Body,
    bool IsHtml);

/// <summary>The IMAP server refused or could not be reached. The message is the server's, for the user.</summary>
public sealed class MailboxReadException(string message) : Exception(message);

/// <summary>
/// Reads a mailbox over IMAP, so an account managed here can be worked from inside the app rather than only
/// in webmail. Read-only apart from marking a message seen as it is opened — the send path is
/// <see cref="IMailSender"/>.
/// </summary>
public interface IMailboxReader
{
    /// <summary>Unread messages in the inbox — the switcher's badge. Cheap: a STATUS, not a full open.</summary>
    Task<int> UnreadCountAsync(MailboxConnection connection, CancellationToken cancellationToken = default);

    /// <summary>The inbox, newest first, one page at a time.</summary>
    Task<IReadOnlyList<MailHeader>> ListInboxAsync(
        MailboxConnection connection,
        int skip,
        int take,
        CancellationToken cancellationToken = default);

    /// <summary>One message in full, optionally marking it read as it is opened. Null if the uid is gone.</summary>
    Task<MailContent?> ReadMessageAsync(
        MailboxConnection connection,
        uint uid,
        bool markSeen,
        CancellationToken cancellationToken = default);
}
