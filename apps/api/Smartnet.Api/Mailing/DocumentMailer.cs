using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Smartnet.Api.Contracts;
using Smartnet.Domain.MasterData;
using Smartnet.Domain.Settings;
using Smartnet.Infrastructure.Persistence;

namespace Smartnet.Api.Mailing;

/// <summary>
/// Emailing a document to a customer's saved contacts — the part every document does the same way.
/// </summary>
/// <remarks>
/// Job sheets, outstanding statements and quotations all need the same three things: the customer's
/// contacts that can receive mail, whether a send is possible at all for the company, and the send
/// itself with the SMTP password decrypted. Written once here rather than a fourth time in the next
/// controller that needs it.
///
/// <para>What stays with each document is the part that differs — its own template, its own audit
/// entry, and its own rule about who may see it.</para>
/// </remarks>
public sealed class DocumentMailer
{
    /// <summary>The one protector purpose allowed to read a stored SMTP password.</summary>
    private const string PasswordProtector = "Smartnet.MailSettings.Password";

    private readonly SmartnetDbContext _db;
    private readonly IMailSender _mail;
    private readonly IDataProtectionProvider _protection;

    public DocumentMailer(SmartnetDbContext db, IMailSender mail, IDataProtectionProvider protection)
    {
        _db = db;
        _mail = mail;
        _protection = protection;
    }

    /// <summary>
    /// The customer's saved contacts that can receive mail, by customer code.
    /// </summary>
    /// <remarks>
    /// By code rather than by key because that is what the legacy documents carry — <c>jobs_m.customer</c>
    /// and <c>quotation_h.customer</c> both hold the code — and a legacy document has to be emailable
    /// without first being adopted.
    ///
    /// <para>Document contacts are pre-selected: they are who the document would have been handed to on
    /// paper. A notifications-only contact is offered but never assumed.</para>
    /// </remarks>
    public async Task<List<DocumentContact>> ContactsByCodeAsync(string? customerCode, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(customerCode))
        {
            return [];
        }

        var code = customerCode.Trim();

        return await _db.CustomerContacts
            .Where(c => c.Customer.Code == code && c.Email != null && c.Email != "")
            .OrderBy(c => c.Name)
            .Select(c => new DocumentContact(
                c.Id,
                c.Name,
                c.Email!,
                c.Usage,
                c.Usage == ContactUsage.DocumentsAndNotifications))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>The same, for a document that carries a real customer key rather than a code.</summary>
    public async Task<List<DocumentContact>> ContactsByCustomerAsync(long customerId, CancellationToken cancellationToken) =>
        await _db.CustomerContacts
            .Where(c => c.CustomerId == customerId && c.Email != null && c.Email != "")
            .OrderBy(c => c.Name)
            .Select(c => new DocumentContact(
                c.Id,
                c.Name,
                c.Email!,
                c.Usage,
                c.Usage == ContactUsage.DocumentsAndNotifications))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <summary>
    /// A supplier's addresses, as selectable contacts.
    /// </summary>
    /// <remarks>
    /// Suppliers have no contacts table of their own — <c>sup_m</c> carries one <c>email</c> column and a
    /// contact name. The column is split on <c>;</c> the way the customer email column was before it was
    /// backfilled, so a supplier who has more than one address on file still gets a list to choose from
    /// rather than only their first.
    ///
    /// <para>Ids are positional (1, 2, 3…) because there are no rows to have keys. The caller re-resolves
    /// them against the same supplier before sending, so a position cannot address anyone else.</para>
    /// </remarks>
    public async Task<List<DocumentContact>> ContactsBySupplierCodeAsync(string? supplierCode, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(supplierCode))
        {
            return [];
        }

        var code = supplierCode.Trim();

        var supplier = await _db.Suppliers
            .Where(s => s.Code == code)
            .Select(s => new { s.Email, s.ContactPerson })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (supplier is null || string.IsNullOrWhiteSpace(supplier.Email))
        {
            return [];
        }

        return [.. supplier.Email
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select((email, index) => new DocumentContact(
                index + 1,
                supplier.ContactPerson,
                email,
                ContactUsage.DocumentsAndNotifications,
                // The first address is the one the legacy app would have used; the rest are offered.
                index == 0))];
    }

    /// <summary>
    /// Why a send would fail, decided before the user picks anybody — null when it would be attempted.
    /// </summary>
    /// <remarks>
    /// Said up front rather than discovered afterwards. The company's kill switch is off by default, and
    /// finding that out after choosing recipients and pressing Send is a worse way to learn it.
    /// </remarks>
    public async Task<string?> BlockedReasonAsync(long companyId, int contactCount, CancellationToken cancellationToken)
    {
        if (contactCount == 0)
        {
            return "This customer has no contact with an email address. Add one on the customer first.";
        }

        var settings = await _db.MailSettings
            .Where(s => s.CompanyId == companyId)
            .Select(s => new { s.Host, s.SendEnabled, s.FromAddress, s.Username })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (settings is null || string.IsNullOrWhiteSpace(settings.Host))
        {
            return "No mail server is configured for this company. Set one in Settings.";
        }

        if (string.IsNullOrWhiteSpace(settings.FromAddress) && string.IsNullOrWhiteSpace(settings.Username))
        {
            return "No from-address is configured for this company. Set one in Settings.";
        }

        return settings.SendEnabled
            ? null
            : "Sending is switched off for this company. Turn it on in Settings to send.";
    }

    /// <summary>
    /// Sends one message for a company, decrypting its SMTP password on the way.
    /// </summary>
    /// <remarks>
    /// Never throws for a refusal: an unconfigured server or a server that says no comes back as
    /// <see cref="MailResult.Sent"/> false with the reason, because "we tried and it bounced" is an
    /// answer the caller has to be able to record.
    /// </remarks>
    public async Task<MailResult> SendAsync(
        long companyId,
        IReadOnlyCollection<string> recipients,
        string subject,
        string htmlBody,
        IReadOnlyCollection<MailAttachment>? attachments,
        CancellationToken cancellationToken)
    {
        var settings = await _db.MailSettings
            .FirstOrDefaultAsync(s => s.CompanyId == companyId, cancellationToken)
            .ConfigureAwait(false);

        if (settings is null)
        {
            return new MailResult(false, "No mail server is configured for this company.");
        }

        var password = string.IsNullOrEmpty(settings.PasswordEncrypted)
            ? null
            : _protection.CreateProtector(PasswordProtector).Unprotect(settings.PasswordEncrypted);

        return await _mail
            .SendAsync(settings, password, recipients, subject, htmlBody, attachments, cancellationToken)
            .ConfigureAwait(false);
    }
}
