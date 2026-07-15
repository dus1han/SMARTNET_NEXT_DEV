using Smartnet.Domain.Auditing;

namespace Smartnet.Domain.Settings;

/// <summary>
/// Per-company outbound mail configuration. Closes ISSUES A2.
/// </summary>
/// <remarks>
/// Today this is <c>new NetworkCredential("info@smart-net.lk", "Admin@2023##")</c>, written into
/// <c>CusOutstandingController.cs</c> twice, shipped with every copy of the source folder.
///
/// <para><b>The password is encrypted at rest and write-only over the API.</b> The settings screen
/// shows <c>••••••</c>; a GET never returns it, not even to a Dev_Admin — because the only reason
/// to read a stored SMTP password back out is to exfiltrate it. To change it you set a new one.</para>
///
/// <para>It is on the audit redaction list, so the audit log records that it changed and never
/// what it changed to.</para>
/// </remarks>
public class MailSettings : IAuditable
{
    public long Id { get; set; }

    public long CompanyId { get; set; }

    public string Host { get; set; } = null!;
    public int Port { get; set; } = 587;
    public bool UseSsl { get; set; } = true;

    public string? Username { get; set; }

    /// <summary>
    /// Encrypted with ASP.NET Data Protection. Never returned by any endpoint.
    /// </summary>
    /// <remarks>
    /// The property name ends in "Encrypted" deliberately: <see cref="AuditRedaction"/> matches on
    /// the name, so a future entity that stores a secret under a similar name is redacted the day
    /// it is written rather than the day somebody remembers.
    /// </remarks>
    public string? PasswordEncrypted { get; set; }

    public string? FromAddress { get; set; }
    public string? FromName { get; set; }
    public string? ReplyTo { get; set; }
    public string? Bcc { get; set; }

    /// <summary>
    /// The kill switch. When false, nothing is sent — it is logged and dropped.
    /// </summary>
    /// <remarks>
    /// Exists so that a restored production backup running in staging cannot email 223 real
    /// customers about their outstanding balances. The legacy app has no such switch, and its
    /// bulk-dunning action mails every debtor in one 16-minute HTTP request.
    /// </remarks>
    public bool SendEnabled { get; set; }

    /// <summary>A cap on messages per day. Zero means no limit.</summary>
    public int DailyLimit { get; set; }

    public long? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public long? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public long? DeletedBy { get; set; }
    public DateTime? DeletedAt { get; set; }
    public int RowVersion { get; set; }
}

/// <summary>
/// A mail body an administrator can reword without a deployment. Closes ISSUES A2b.
/// </summary>
/// <remarks>
/// The five legacy mail flows (<c>emailIPDF</c>, <c>emailQPDF</c>, <c>emailPOPDF</c>,
/// <c>emailOS</c>, <c>emailOSBulk</c>) each have their subject and body written into C#. Finance
/// cannot soften the wording of a dunning letter without a developer and a release.
/// </remarks>
public class EmailTemplate : IAuditable, ISoftDeletable
{
    public long Id { get; set; }

    public long CompanyId { get; set; }

    /// <summary>One of <see cref="EmailTemplateKeys"/>.</summary>
    public string TemplateKey { get; set; } = null!;

    public string Subject { get; set; } = null!;

    /// <summary>
    /// Body with <c>{{token}}</c> substitution — <c>{{invoice_no}}</c>, <c>{{customer_name}}</c>,
    /// <c>{{total}}</c>, <c>{{due_date}}</c>.
    /// </summary>
    public string Body { get; set; } = null!;

    public long? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public long? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public long? DeletedBy { get; set; }
    public DateTime? DeletedAt { get; set; }
    public int RowVersion { get; set; }
}

public static class EmailTemplateKeys
{
    public const string InvoiceSent = "invoice_sent";
    public const string QuotationSent = "quotation_sent";
    public const string PurchaseOrderSent = "purchase_order_sent";
    public const string OutstandingReminder = "outstanding_reminder";
    public const string OutstandingBulk = "outstanding_bulk";

    public static readonly IReadOnlyList<string> All =
    [
        InvoiceSent, QuotationSent, PurchaseOrderSent, OutstandingReminder, OutstandingBulk,
    ];

    public static bool IsKnown(string key) => All.Contains(key);
}

/// <summary>
/// A record of what was actually sent. Closes ISSUES A2c.
/// </summary>
/// <remarks>
/// Nothing currently logs outbound mail, so "did the customer ever receive their invoice?" is
/// unanswerable. It is asked often.
/// </remarks>
public class EmailLogEntry
{
    public long Id { get; set; }

    public long? CompanyId { get; set; }

    public string Recipient { get; set; } = null!;
    public string? TemplateKey { get; set; }

    /// <summary>e.g. "INVOICE:SN-INV-00042" — what this message was about.</summary>
    public string? DocumentRef { get; set; }

    public string Status { get; set; } = null!;

    /// <summary>The provider's failure, when there was one. Not shown to customers.</summary>
    public string? Error { get; set; }

    public DateTime SentAt { get; set; }

    public long? SentBy { get; set; }
}
