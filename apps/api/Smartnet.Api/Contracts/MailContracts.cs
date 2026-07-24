using FluentValidation;

namespace Smartnet.Api.Contracts;

// The user-facing mail screen — the mailboxes assigned to the signed-in user, read over IMAP, sent over
// SMTP. Distinct from MailAccountContracts, which is the administration of the mailboxes themselves.

/// <summary>A mailbox in the switcher — the signed-in user's, with its unread count when reachable.</summary>
/// <param name="Unread">Null when the count could not be read; <paramref name="Error"/> says why.</param>
public sealed record MailboxListItem(long Id, string DisplayName, string EmailAddress, int? Unread, string? Error);

/// <summary>A folder in the open mailbox. <paramref name="Role"/> is the well-known kind (Inbox, Sent, …).</summary>
public sealed record MailFolderResponse(string FullName, string Name, string Role, int Unread);

/// <summary>One row in the inbox list — no body, so the list is one cheap fetch.</summary>
public sealed record MailHeaderResponse(
    uint Uid,
    string FromName,
    string FromAddress,
    string Subject,
    DateTimeOffset Date,
    bool Seen,
    bool HasAttachments);

/// <summary>A message opened for reading. <see cref="IsHtml"/> says how the client should render the body.</summary>
/// <param name="Text">A plain-text rendering, for quoting into a reply or forward.</param>
public sealed record MailMessageResponse(
    uint Uid,
    string FromName,
    string FromAddress,
    string To,
    string Subject,
    DateTimeOffset Date,
    string Body,
    bool IsHtml,
    string Text);

/// <summary>Compose or reply. <paramref name="To"/> may be several addresses, comma- or semicolon-separated.</summary>
public sealed record SendMailRequest(string To, string Subject, string Body);

public sealed class SendMailRequestValidator : AbstractValidator<SendMailRequest>
{
    public SendMailRequestValidator()
    {
        RuleFor(r => r.To).NotEmpty().WithMessage("Enter at least one recipient.");
        RuleFor(r => r.Subject).MaximumLength(255);
    }
}
