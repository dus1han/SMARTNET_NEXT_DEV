using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using Smartnet.Domain.Settings;

namespace Smartnet.Infrastructure.Settings;

/// <inheritdoc cref="IMailSender"/>
public sealed class MailSender : IMailSender
{
    public Task<MailResult> SendTestAsync(
        MailSettings settings,
        string? password,
        string recipient,
        CancellationToken cancellationToken = default) =>
        SendMessageAsync(
            settings,
            password,
            recipient,
            "SMARTNET test message",
            new TextPart("plain")
            {
                Text = "This is a test message from SMARTNET. If you are reading it, "
                     + "outbound mail is configured correctly.",
            },
            cancellationToken);

    public Task<MailResult> SendAsync(
        MailSettings settings,
        string? password,
        string recipient,
        string subject,
        string htmlBody,
        CancellationToken cancellationToken = default) =>
        SendMessageAsync(settings, password, recipient, subject, new TextPart("html") { Text = htmlBody }, cancellationToken);

    private static async Task<MailResult> SendMessageAsync(
        MailSettings settings,
        string? password,
        string recipient,
        string subject,
        MimeEntity body,
        CancellationToken cancellationToken)
    {
        // The kill switch, honoured on every path — test and dunning alike.
        //
        // This exists so that a restored production backup running in staging cannot mail 223 real
        // customers about their outstanding balances, and it is what keeps bulk dunning gated until the
        // business turns it on. If any send path could bypass it, the switch would be off by exactly
        // the one path somebody would use to "just check".
        if (!settings.SendEnabled)
        {
            return new MailResult(
                Sent: false,
                Error: "Sending is switched off for this company. Turn it on to send.");
        }

        // A message with no sender is rejected by the server anyway, with a message far less
        // helpful than this one.
        var from = settings.FromAddress ?? settings.Username;

        if (string.IsNullOrWhiteSpace(from))
        {
            return new MailResult(
                Sent: false,
                Error: "No from-address is configured. Set one before sending.");
        }

        var message = new MimeMessage();

        message.From.Add(new MailboxAddress(settings.FromName ?? string.Empty, from));
        message.To.Add(MailboxAddress.Parse(recipient));

        if (!string.IsNullOrWhiteSpace(settings.Bcc))
        {
            message.Bcc.Add(MailboxAddress.Parse(settings.Bcc));
        }

        if (!string.IsNullOrWhiteSpace(settings.ReplyTo))
        {
            message.ReplyTo.Add(MailboxAddress.Parse(settings.ReplyTo));
        }

        message.Subject = subject;
        message.Body = body;

        using var client = new SmtpClient();

        try
        {
            await client.ConnectAsync(
                settings.Host,
                settings.Port,
                settings.UseSsl ? SecureSocketOptions.StartTlsWhenAvailable : SecureSocketOptions.None,
                cancellationToken).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(settings.Username) && !string.IsNullOrEmpty(password))
            {
                await client.AuthenticateAsync(settings.Username, password, cancellationToken)
                    .ConfigureAwait(false);
            }

            await client.SendAsync(message, cancellationToken).ConfigureAwait(false);
            await client.DisconnectAsync(quit: true, cancellationToken).ConfigureAwait(false);

            return new MailResult(Sent: true);
        }
        catch (Exception ex) when (ex is SmtpCommandException or SmtpProtocolException
                                      or AuthenticationException or IOException)
        {
            // Caught narrowly and reported, rather than thrown: "the mail server rejected this" is
            // an answer the administrator can act on, not a 500 with a correlation id.
            return new MailResult(Sent: false, Error: ex.Message);
        }
    }
}
