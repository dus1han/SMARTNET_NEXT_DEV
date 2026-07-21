using System.ComponentModel.DataAnnotations;

namespace Smartnet.Infrastructure.Backups;

/// <summary>
/// The parts of backup configuration that belong to the <b>deployment</b>, not to the screen.
/// </summary>
/// <remarks>
/// The FTP destination is configured in the UI and stored in <c>backup_settings</c> — an administrator
/// changing where backups go should not need a deploy. What is here instead is the two things that must
/// not be editable from a web form:
/// <list type="bullet">
/// <item><see cref="RestoreConnectionString"/>, which can drop the schema.</item>
/// <item>The paths of the client binaries and the timeout, which describe the container rather than the
/// business.</item>
/// </list>
/// </remarks>
public sealed class BackupOptions
{
    public const string Section = "Backup";

    /// <summary>
    /// A connection string with enough privilege to <b>drop and recreate</b> the schema — restore only.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately separate from <c>ConnectionStrings:Smartnet</c>, and deliberately not in the settings
    /// screen. The application's own user holds SELECT, INSERT and per-table UPDATE/DELETE and <i>no DDL
    /// whatsoever</i> — the hardening from <c>infra/sql/narrow-app-user-grants.sh</c>, which is also what
    /// makes <c>audit_log</c> genuinely append-only. A restore needs DROP and CREATE, so it cannot use
    /// that user.
    /// </para>
    /// <para>
    /// Empty means restore is unavailable, and the endpoints say so plainly. That is a supported
    /// deployment: scheduled backups, manual backups and downloads all work without it. A site that does
    /// not want a restore button reachable from the internet simply never sets it.
    /// </para>
    /// </remarks>
    public string RestoreConnectionString { get; set; } = string.Empty;

    /// <summary>The MariaDB client binaries. Present in the API image; see apps/api/Dockerfile.</summary>
    public string DumpCommand { get; set; } = "mysqldump";

    public string RestoreCommand { get; set; } = "mysql";

    /// <summary>How long a dump or a restore may run before it is abandoned.</summary>
    [Range(1, 240)]
    public int TimeoutMinutes { get; set; } = 30;

    /// <summary>How often the scheduled backup runs. Hourly.</summary>
    [Range(1, 168)]
    public int IntervalHours { get; set; } = 1;
}
