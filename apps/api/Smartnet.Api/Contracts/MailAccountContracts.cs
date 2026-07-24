using FluentValidation;
using Smartnet.Domain.Settings;

namespace Smartnet.Api.Contracts;

// --- The shared server ---------------------------------------------------------------------------

/// <summary>
/// The shared mail server and the cPanel connection — the Dev-Admin "cPanel" screen. The API token is never
/// returned, only whether one is set.
/// </summary>
public sealed record MailServerSettingsResponse(
    string? MailDomain,
    string OutgoingHost,
    int OutgoingPort,
    bool OutgoingUseSsl,
    string IncomingProtocol,
    string IncomingHost,
    int IncomingPort,
    bool IncomingUseSsl,
    string? CpanelHost,
    int CpanelPort,
    string? CpanelUsername,
    bool HasCpanelApiToken);

/// <param name="CpanelApiToken">Null leaves the stored token alone; a value replaces it. Never read back.</param>
public sealed record SaveMailServerSettingsRequest(
    string? MailDomain,
    string OutgoingHost,
    int OutgoingPort,
    bool OutgoingUseSsl,
    string IncomingProtocol,
    string IncomingHost,
    int IncomingPort,
    bool IncomingUseSsl,
    string? CpanelHost,
    int CpanelPort,
    string? CpanelUsername,
    string? CpanelApiToken);

/// <summary>The mail domain, for the add screen's fixed suffix — readable without the cPanel-screen permission.</summary>
public sealed record MailDomainResponse(string? Domain);

public sealed class SaveMailServerSettingsRequestValidator : AbstractValidator<SaveMailServerSettingsRequest>
{
    public SaveMailServerSettingsRequestValidator()
    {
        RuleFor(r => r.OutgoingHost).NotEmpty().MaximumLength(200);
        RuleFor(r => r.OutgoingPort).InclusiveBetween(1, 65535);
        RuleFor(r => r.IncomingHost).NotEmpty().MaximumLength(200);
        RuleFor(r => r.IncomingPort).InclusiveBetween(1, 65535);
        RuleFor(r => r.IncomingProtocol)
            .Must(IncomingMailProtocols.IsKnown)
            .WithMessage("Incoming protocol must be IMAP or POP3.");

        // cPanel is all-or-nothing: a host without a username (or the reverse) can never authenticate, so it
        // is a half-configured connection that would only fail at use. There is no separate on/off switch —
        // a complete connection is an active one, so this is where "either both or neither" is enforced.
        RuleFor(r => r.CpanelUsername).NotEmpty()
            .When(r => !string.IsNullOrWhiteSpace(r.CpanelHost))
            .WithMessage("A cPanel username is required alongside the host.");
        RuleFor(r => r.CpanelHost).NotEmpty()
            .When(r => !string.IsNullOrWhiteSpace(r.CpanelUsername))
            .WithMessage("A cPanel host is required alongside the username.");
        RuleFor(r => r.CpanelHost).MaximumLength(200);
        RuleFor(r => r.CpanelUsername).MaximumLength(100);
        RuleFor(r => r.CpanelPort).InclusiveBetween(1, 65535);
    }
}

// --- One mailbox ---------------------------------------------------------------------------------

/// <summary>
/// One managed mailbox as the list shows it. The password is never here — only whether one is set — for the
/// same reason as the per-company mail settings: reading a stored SMTP password back over an API serves only
/// to steal it.
/// </summary>
public sealed record MailAccountResponse(
    long Id,
    string DisplayName,
    string EmailAddress,
    bool HasPassword,
    bool Enabled);

/// <param name="Password">
/// Null leaves the stored password as it is — what the edit form sends when it is not retyped. A value
/// replaces it. Neither is ever read back.
/// </param>
public sealed record SaveMailAccountRequest(
    string DisplayName,
    string EmailAddress,
    string? Password,
    bool Enabled);

public sealed class SaveMailAccountRequestValidator : AbstractValidator<SaveMailAccountRequest>
{
    public SaveMailAccountRequestValidator()
    {
        RuleFor(r => r.DisplayName).NotEmpty().MaximumLength(100);
        RuleFor(r => r.EmailAddress).NotEmpty().MaximumLength(200).EmailAddress();
    }
}
