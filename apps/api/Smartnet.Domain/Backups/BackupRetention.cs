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
    /// <para>
    /// Ordered by the <b>stamp inside the name</b>, not by the modified time the server reports and not by
    /// the name as a whole. FTP timestamps are a well-known swamp — server-local, minute-granular on some
    /// daemons, and occasionally the upload time rather than the file's — whereas the stamp is one we
    /// wrote ourselves in UTC.
    /// </para>
    /// <para>
    /// It used to sort on the whole name, which reads correctly and is wrong: the kind precedes the stamp,
    /// so <c>manual</c> outranks <c>auto</c> regardless of age. Fifteen manual backups would have filled
    /// the rotation and left every hourly backup to be deleted immediately after it was uploaded.
    /// </para>
    /// <para>
    /// Anything this cannot read a stamp out of is left strictly alone rather than treated as oldest and
    /// deleted first. This removes only files it can prove it created.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<BackupFile> ToPrune(IEnumerable<BackupFile> files, int keep)
    {
        if (keep < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(keep), keep, "Retention cannot be negative.");
        }

        return [.. files
            .Select(f => (File: f, TakenUtc: BackupNaming.TakenAtUtc(f.Name)))
            .Where(entry => BackupNaming.IsBackupName(entry.File.Name) && entry.TakenUtc is not null)
            .OrderByDescending(entry => entry.TakenUtc!.Value)
            // Two backups sharing a second is not a thing that happens, but ties have to break somewhere
            // and an unstable order in the code that deletes things is not worth the argument.
            .ThenByDescending(entry => entry.File.Name, StringComparer.Ordinal)
            .Skip(keep)
            .Select(entry => entry.File)];
    }
}
