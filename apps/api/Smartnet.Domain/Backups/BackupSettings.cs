using Smartnet.Domain.Auditing;

namespace Smartnet.Domain.Backups;

/// <summary>
/// Where backups are sent — configured from the screen, not from a deployment.
/// </summary>
/// <remarks>
/// <para>
/// <b>One row, no company.</b> Everything else in settings belongs to a trading entity; a backup does
/// not. It is a dump of the whole database, both companies and the audit log together, so a per-company
/// destination would be a category error — there is only one database to back up.
/// </para>
/// <para>
/// <b>The password is encrypted at rest and write-only over the API</b>, exactly as the SMTP password is:
/// the screen shows that one is set, a GET never returns it, and to change it you set a new one. The
/// property name ends in <c>Encrypted</c> on purpose — <see cref="AuditRedaction"/> matches on the name,
/// so the audit log records that it changed and never what it changed to.
/// </para>
/// <para>
/// What is deliberately <b>not</b> here: the privileged database credential a restore runs under. That
/// one can drop the schema, and a form on a web page is the wrong place to keep something that can
/// destroy the business's records — it stays in the server environment file, where the connection string
/// and the signing key already live. See <c>BackupOptions.RestoreConnectionString</c>.
/// </para>
/// </remarks>
public class BackupSettings : IAuditable
{
    public long Id { get; set; }

    /// <summary>Turns the hourly job on. Off until a destination is configured and tested.</summary>
    public bool Enabled { get; set; }

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 21;

    public string? Username { get; set; }

    /// <summary>Encrypted with ASP.NET Data Protection. Never returned by any endpoint.</summary>
    public string? PasswordEncrypted { get; set; }

    /// <summary>FTPS — encrypt the connection. See the remarks on the settings contract.</summary>
    public bool UseTls { get; set; } = true;

    /// <summary>Accept a self-signed certificate. Off by default; it costs MITM protection.</summary>
    public bool AcceptAnyCertificate { get; set; }

    /// <summary>The folder the rotation lives in.</summary>
    public string RemotePath { get; set; } = "/";

    /// <summary>Where pre-restore safety copies go — a different folder, so the rotation cannot prune them.</summary>
    public string SafetyPath { get; set; } = "/pre-restore";

    /// <summary>How many backups the rotation keeps.</summary>
    public int Retention { get; set; } = 15;

    public long? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public long? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public long? DeletedBy { get; set; }
    public DateTime? DeletedAt { get; set; }
    public int RowVersion { get; set; }
}
