namespace Smartnet.Domain.Settings;

/// <summary>
/// How to reach one IMAP mailbox — the shared incoming server, and the mailbox's own login, already
/// decrypted. Decryption happens once, at the controller allowed to do it; the reader never sees ciphertext
/// and never touches the database, exactly like <see cref="IMailSender"/>.
/// </summary>
public sealed record MailboxConnection(string Host, int Port, bool UseSsl, string EmailAddress, string Password);

/// <summary>The well-known role of a folder, so the client can show and order them without guessing names.</summary>
public enum MailFolderRole
{
    Inbox,
    Sent,
    Drafts,
    Trash,
    Junk,
    Archive,
    Other,
}

/// <summary>One folder in the mailbox — its path, a display name, its role, and its unread count.</summary>
public sealed record MailFolderInfo(string FullName, string Name, MailFolderRole Role, int Unread);

/// <summary>A message as it appears in a folder list — envelope and flags only, no body fetched.</summary>
public sealed record MailHeader(
    uint Uid,
    string FromName,
    string FromAddress,
    string Subject,
    DateTimeOffset Date,
    bool Seen,
    bool HasAttachments);

/// <summary>A single message opened for reading. <see cref="IsHtml"/> says how to render <see cref="Body"/>.</summary>
/// <param name="Text">
/// A plain-text rendering of the same message, for quoting into a reply or forward — the compose box is
/// plain text, so the formatted body cannot be dropped into it as-is.
/// </param>
public sealed record MailContent(
    uint Uid,
    string FromName,
    string FromAddress,
    string To,
    string Subject,
    DateTimeOffset Date,
    string Body,
    bool IsHtml,
    string Text);

/// <summary>What to append to the Sent folder after a message goes out.</summary>
public sealed record SentMessage(
    string FromName,
    string FromAddress,
    IReadOnlyCollection<string> To,
    string Subject,
    string HtmlBody);

/// <summary>The IMAP server refused or could not be reached. The message is the server's, for the user.</summary>
public sealed class MailboxReadException(string message) : Exception(message);

/// <summary>
/// Reads and organises a mailbox over IMAP, so an account managed here can be worked from inside the app
/// rather than only in webmail — the send path is <see cref="IMailSender"/>.
/// </summary>
public interface IMailboxReader
{
    /// <summary>Unread messages in the inbox — the switcher's badge. Cheap: a STATUS, not a full open.</summary>
    Task<int> UnreadCountAsync(MailboxConnection connection, CancellationToken cancellationToken = default);

    /// <summary>The mailbox's folders, each with its unread count, ordered by role then name.</summary>
    Task<IReadOnlyList<MailFolderInfo>> ListFoldersAsync(MailboxConnection connection, CancellationToken cancellationToken = default);

    /// <summary>One folder's messages, newest first, one page at a time.</summary>
    Task<IReadOnlyList<MailHeader>> ListMessagesAsync(
        MailboxConnection connection,
        string folder,
        int skip,
        int take,
        CancellationToken cancellationToken = default);

    /// <summary>One message in full, optionally marking it read as it is opened. Null if the uid is gone.</summary>
    Task<MailContent?> ReadMessageAsync(
        MailboxConnection connection,
        string folder,
        uint uid,
        bool markSeen,
        CancellationToken cancellationToken = default);

    /// <summary>Marks a message read or unread.</summary>
    Task SetSeenAsync(
        MailboxConnection connection,
        string folder,
        uint uid,
        bool seen,
        CancellationToken cancellationToken = default);

    /// <summary>Moves a message to Trash — or, if it is already in Trash, deletes it for good.</summary>
    Task DeleteAsync(
        MailboxConnection connection,
        string folder,
        uint uid,
        CancellationToken cancellationToken = default);

    /// <summary>Files a copy of an outbound message in the Sent folder, so sending leaves a record.</summary>
    Task AppendToSentAsync(
        MailboxConnection connection,
        SentMessage message,
        CancellationToken cancellationToken = default);
}
