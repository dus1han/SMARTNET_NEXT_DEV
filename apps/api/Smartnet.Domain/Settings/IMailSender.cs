namespace Smartnet.Domain.Settings;

/// <param name="Sent">False if the server refused it, or if the kill switch is off.</param>
/// <param name="Error">The provider's message. For the administrator, never for a customer.</param>
public sealed record MailResult(bool Sent, string? Error = null);

/// <summary>One file attached to a message — held in memory, because the documents this system
/// sends are single-digit-kilobyte PDFs it has just rendered, never anything streamed from disk.</summary>
public sealed record MailAttachment(string FileName, string ContentType, byte[] Content);

public interface IMailSender
{
    /// <summary>
    /// Sends a test message, so that a misconfigured mail server is discovered by the person
    /// configuring it rather than by a customer who never received their invoice.
    /// </summary>
    /// <param name="password">
    /// Passed in already decrypted. The sender never touches the database and never sees the
    /// ciphertext — decryption happens once, at the one place that is allowed to do it.
    /// </param>
    Task<MailResult> SendTestAsync(
        MailSettings settings,
        string? password,
        string recipient,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends one real message — the path the dunning queue uses. Honours the same
    /// <see cref="MailSettings.SendEnabled"/> kill switch as the test: when it is off, nothing leaves
    /// the building and the caller is told so, which is exactly how bulk dunning stays gated until the
    /// business turns it on.
    /// </summary>
    /// <param name="password">Already decrypted, by the one caller allowed to (see the test send).</param>
    /// <param name="recipients">
    /// One or more addresses, all on the same message. A job sheet goes to the customer's contacts
    /// together — sending each their own copy would tell them nothing and cost n sends.
    /// </param>
    /// <param name="attachments">
    /// Optional files. The document being sent <i>is</i> the point of most of these messages, so a
    /// send path that could not carry one would just be a differently-shaped notification.
    /// </param>
    Task<MailResult> SendAsync(
        MailSettings settings,
        string? password,
        IReadOnlyCollection<string> recipients,
        string subject,
        string htmlBody,
        IReadOnlyCollection<MailAttachment>? attachments = null,
        CancellationToken cancellationToken = default);
}
