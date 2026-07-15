using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using Smartnet.Domain.Settings;

namespace Smartnet.Infrastructure.Settings;

/// <inheritdoc cref="IMailSender"/>
public sealed class MailSender : IMailSender
{
    public async Task<MailResult> SendTestAsync(
        MailSettings settings,
        string? password,
        string recipient,
        CancellationToken cancellationToken = default)
    {
        // The kill switch, honoured even for a test.
        //
        // This exists so that a restored production backup running in staging cannot mail 223 real
        // customers about their outstanding balances. If a test message could bypass it, the switch
        // would be off by exactly the one path somebody would use to "just check".
        if (!settings.SendEnabled)
        {
            return new MailResult(
                Sent: false,
                Error: "Sending is switched off for this company. Turn it on to send a test.");
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
        message.Subject = "SMARTNET test message";

        message.Body = new TextPart("plain")
        {
            Text = "This is a test message from SMARTNET. If you are reading it, "
                 + "outbound mail is configured correctly.",
        };

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
