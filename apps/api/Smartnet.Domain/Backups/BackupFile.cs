using System.Globalization;

namespace Smartnet.Domain.Backups;

/// <summary>Why a backup was taken. It decides where it is stored and whether it is ever pruned.</summary>
public enum BackupKind
{
    /// <summary>The hourly job.</summary>
    Scheduled,

    /// <summary>Someone pressed the button.</summary>
    Manual,

    /// <summary>
    /// Taken automatically immediately before a restore, as the undo for it.
    /// </summary>
    /// <remarks>
    /// Kept apart from the others and <b>never pruned</b>. It is the only thing standing between a restore
    /// of the wrong file and permanent loss, and the rotation would otherwise age it out within fifteen
    /// hours — which is precisely the window in which somebody realises what they have done.
    /// </remarks>
    PreRestore,
}

/// <summary>One backup as it sits on the remote store.</summary>
public sealed record BackupFile(string Name, long SizeBytes, DateTime ModifiedUtc);

/// <summary>
/// What a backup is called. The name carries the kind and the moment, because the store is a flat
/// directory and the filename is the only metadata that survives a round trip through FTP.
/// </summary>
/// <remarks>
/// Sortable by name because the timestamp is fixed-width and most-significant-first, so "newest fifteen"
/// is an ordering question rather than a stat() of every file. UTC, for the same reason every other
/// timestamp here is: a backup named in server-local time is ambiguous for one hour every autumn.
/// </remarks>
public static class BackupNaming
{
    private const string Stamp = "yyyyMMdd-HHmmss";

    public static string For(BackupKind kind, DateTime utcNow) =>
        $"smartnet-{Suffix(kind)}-{utcNow.ToString(Stamp, CultureInfo.InvariantCulture)}.sql.gz";

    private static string Suffix(BackupKind kind) => kind switch
    {
        BackupKind.Scheduled => "auto",
        BackupKind.Manual => "manual",
        BackupKind.PreRestore => "prerestore",
        _ => "auto",
    };

    /// <summary>
    /// Whether a name is one of ours, and safe to use as a remote path segment.
    /// </summary>
    /// <remarks>
    /// This is the guard on every caller-supplied name — download and restore both take one from the
    /// client. Anything with a separator or a traversal in it is refused rather than sanitised, because a
    /// name that needs cleaning is not a name we wrote.
    /// </remarks>
    public static bool IsBackupName(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && name.StartsWith("smartnet-", StringComparison.Ordinal)
        && name.EndsWith(".sql.gz", StringComparison.Ordinal)
        && name.IndexOfAny(['/', '\\']) < 0
        && !name.Contains("..", StringComparison.Ordinal);
}
