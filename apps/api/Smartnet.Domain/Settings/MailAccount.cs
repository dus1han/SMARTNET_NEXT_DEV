using Smartnet.Domain.Auditing;

namespace Smartnet.Domain.Settings;

/// <summary>
/// The mail server every account shares — the cPanel host and its ports, entered once.
/// </summary>
/// <remarks>
/// A single global row. Every mailbox on the same cPanel reaches the same SMTP and IMAP/POP3 server; only
/// the login differs per account (see <see cref="MailAccount"/>). Holding the server here means correcting
/// a port once fixes it for every account, rather than editing each one.
/// </remarks>
public class MailServerSettings : IAuditable
{
    public long Id { get; set; }

    /// <summary>
    /// The mail domain every mailbox lives on — "smart-net.lk". Shown as a fixed suffix when adding an
    /// account (type the name, get the address) and used as the cPanel domain when a mailbox is provisioned.
    /// </summary>
    public string? MailDomain { get; set; }

    // Outgoing (SMTP)
    public string OutgoingHost { get; set; } = null!;
    public int OutgoingPort { get; set; } = 587;
    public bool OutgoingUseSsl { get; set; } = true;

    // Incoming (IMAP / POP3)
    /// <summary>"IMAP" or "POP3".</summary>
    public string IncomingProtocol { get; set; } = "IMAP";
    public string IncomingHost { get; set; } = null!;
    public int IncomingPort { get; set; } = 993;
    public bool IncomingUseSsl { get; set; } = true;

    // --- cPanel provisioning ---------------------------------------------------------------------
    // Once the host, username and token below are all set, adding an account (or changing its password) here
    // also creates/updates the real mailbox on the host via cPanel's API, so it appears in cPanel/Roundcube.
    // There is no separate on/off switch: a configured connection is an active one. See ICpanelMailProvisioner.

    /// <summary>The cPanel host, no scheme — e.g. "mail.smart-net.lk". Reached over HTTPS on the port below.</summary>
    public string? CpanelHost { get; set; }

    /// <summary>cPanel's UAPI port — 2083 by default.</summary>
    public int CpanelPort { get; set; } = 2083;

    /// <summary>The cPanel account username the API token belongs to.</summary>
    public string? CpanelUsername { get; set; }

    /// <summary>The cPanel API token, encrypted at rest. Never returned by any endpoint.</summary>
    public string? CpanelApiTokenEncrypted { get; set; }

    public long? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public long? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public long? DeletedBy { get; set; }
    public DateTime? DeletedAt { get; set; }
    public int RowVersion { get; set; }
}

/// <summary>
/// One managed mailbox — a cPanel email account. Just its identity and login; the server it reaches is the
/// shared <see cref="MailServerSettings"/>.
/// </summary>
/// <remarks>
/// <para>
/// A <b>global</b> catalogue an administrator maintains under Administration → Mail accounts: add an account,
/// correct it, disable it, remove it. Nothing sends or reads through it yet — that is a later decision — so
/// this is deliberately just the account and its lifecycle.
/// </para>
/// <para>
/// The password is encrypted at rest and write-only — a GET returns only whether one is set — and its name
/// ends in "Encrypted" so <see cref="AuditRedaction"/> records that it changed and never what to.
/// </para>
/// </remarks>
public class MailAccount : IAuditable, ISoftDeletable
{
    public long Id { get; set; }

    /// <summary>The sender/display name recipients see — and the label in the list. "Smart Net Sales".</summary>
    public string DisplayName { get; set; } = null!;

    /// <summary>The mailbox address. Serves as the login username and the from-address both.</summary>
    public string EmailAddress { get; set; } = null!;

    /// <summary>Encrypted with ASP.NET Data Protection. Never returned by any endpoint.</summary>
    public string? PasswordEncrypted { get; set; }

    /// <summary>
    /// Whether the account is active. Disabling it is the reversible alternative to deleting it — the row and
    /// its history stay, it is simply marked unusable.
    /// </summary>
    public bool Enabled { get; set; } = true;

    public long? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public long? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public long? DeletedBy { get; set; }
    public DateTime? DeletedAt { get; set; }
    public int RowVersion { get; set; }
}

/// <summary>The protocols a mail account may use to collect incoming mail.</summary>
public static class IncomingMailProtocols
{
    public const string Imap = "IMAP";
    public const string Pop3 = "POP3";

    public static readonly IReadOnlyList<string> All = [Imap, Pop3];

    public static bool IsKnown(string? value) => value is not null && All.Contains(value);
}
