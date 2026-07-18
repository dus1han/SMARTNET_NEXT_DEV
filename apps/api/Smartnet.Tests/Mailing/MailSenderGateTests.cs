using FluentAssertions;
using Smartnet.Domain.Settings;
using Smartnet.Infrastructure.Settings;

namespace Smartnet.Tests.Mailing;

/// <summary>
/// The kill switch is the gate that holds bulk dunning until the balances are corrected. These pin that
/// it is honoured on the real send path (not only the test path), and that both short-circuit before
/// touching a network — so a disabled company can never leak a message.
/// </summary>
public sealed class MailSenderGateTests
{
    private static readonly MailSender Sender = new();

    private static MailSettings Settings(bool sendEnabled) => new()
    {
        CompanyId = 1,
        Host = "mail.example.com",
        Port = 587,
        FromAddress = "accounts@example.com",
        SendEnabled = sendEnabled,
    };

    [Fact]
    public async Task Send_is_refused_when_the_kill_switch_is_off()
    {
        var result = await Sender.SendAsync(
            Settings(sendEnabled: false), password: null, recipients: ["debtor@example.com"],
            subject: "Outstanding", htmlBody: "<p>hi</p>");

        result.Sent.Should().BeFalse();
        result.Error.Should().Contain("switched off");
    }

    [Fact]
    public async Task Test_send_is_refused_when_the_kill_switch_is_off()
    {
        var result = await Sender.SendTestAsync(Settings(sendEnabled: false), password: null, recipient: "a@b.com");

        result.Sent.Should().BeFalse();
    }

    [Fact]
    public async Task Send_is_refused_when_no_from_address_is_configured()
    {
        var settings = Settings(sendEnabled: true);
        settings.FromAddress = null;
        settings.Username = null;

        var result = await Sender.SendAsync(
            settings, password: null, recipients: ["debtor@example.com"], subject: "s", htmlBody: "b");

        result.Sent.Should().BeFalse();
        result.Error.Should().Contain("from-address");
    }
}
