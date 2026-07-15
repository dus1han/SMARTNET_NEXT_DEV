namespace Smartnet.Domain.Settings;

/// <param name="Sent">False if the server refused it, or if the kill switch is off.</param>
/// <param name="Error">The provider's message. For the administrator, never for a customer.</param>
public sealed record MailResult(bool Sent, string? Error = null);

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
}
