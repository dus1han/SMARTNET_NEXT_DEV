namespace Smartnet.Domain.Backups;

/// <summary>
/// Which backups the rotation drops. Keep the newest <c>N</c>; everything older goes.
/// </summary>
/// <remarks>
/// <para>
/// A pure function, deliberately, because it is the part that deletes things. Given the same listing it
/// always names the same victims, and it can be tested without an FTP server — which is the only way to
/// have any confidence in code whose job is to remove backups.
/// </para>
/// <para>
/// Pre-restore backups are not its business: they live in a separate folder and are never listed here.
/// See <see cref="BackupKind.PreRestore"/>.
/// </para>
/// </remarks>
public static class BackupRetention
{
    /// <summary>
    /// The files to delete so that only the newest <paramref name="keep"/> remain, newest-first by name.
    /// </summary>
    /// <remarks>
    /// Ordered by <b>name</b>, not by the modified time the server reports. FTP timestamps are a
    /// well-known swamp — server-local, minute-granular on some daemons, and occasionally the upload time
    /// rather than the file's — whereas our own name carries a fixed-width UTC stamp we wrote ourselves.
    /// Anything that is not one of ours is left strictly alone: this deletes only files it can prove it
    /// created.
    /// </remarks>
    public static IReadOnlyList<BackupFile> ToPrune(IEnumerable<BackupFile> files, int keep)
    {
        if (keep < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(keep), keep, "Retention cannot be negative.");
        }

        return [.. files
            .Where(f => BackupNaming.IsBackupName(f.Name))
            .OrderByDescending(f => f.Name, StringComparer.Ordinal)
            .Skip(keep)];
    }
}
