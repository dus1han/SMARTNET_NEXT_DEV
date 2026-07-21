using System.Globalization;
using System.Text.RegularExpressions;

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
/// <para>
/// The stamp is fixed-width, most-significant-first and UTC — the last because a backup named in
/// server-local time is ambiguous for one hour every autumn.
/// </para>
/// <para>
/// <b>Sort on <see cref="TakenAtUtc"/>, never on the name.</b> The kind sits in front of the stamp, so
/// an ordinal sort of the whole name orders by kind first and time second: every <c>manual</c> outranks
/// every <c>auto</c>, whenever it was taken. On the listing that showed a stale manual backup above two
/// fresh hourly ones. In the rotation it was worse — enough manual backups would have occupied all
/// fifteen kept slots and deleted each scheduled backup moments after it was uploaded.
/// </para>
/// </remarks>
public static partial class BackupNaming
{
    private const string Stamp = "yyyyMMdd-HHmmss";

    /// <summary>The moment a backup names, or null when the name carries no readable stamp.</summary>
    /// <remarks>
    /// Read from the name rather than from the store's own modified time, which is a swamp: server-local
    /// on some daemons, minute-granular on others, and occasionally the upload time rather than the
    /// file's. This stamp is one we wrote ourselves, in UTC.
    /// </remarks>
    public static DateTime? TakenAtUtc(string? name)
    {
        var match = name is null ? Match.Empty : Stamped().Match(name);

        if (!match.Success)
        {
            return null;
        }

        return DateTime.TryParseExact(
            match.Groups["stamp"].Value,
            Stamp,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var taken)
            ? taken
            : null;
    }

    [GeneratedRegex(@"-(?<stamp>\d{8}-\d{6})\.sql\.gz$", RegexOptions.CultureInvariant)]
    private static partial Regex Stamped();

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
