namespace Smartnet.Domain.Backups;

/// <summary>
/// Where backups live — the remote store, behind an interface.
/// </summary>
/// <remarks>
/// FTPS today. An interface because the alternative is <c>FtpClient</c> woven through the service, the
/// controller and the scheduler, and then the day the business moves to S3 or a mounted volume every one
/// of them changes. It also makes the orchestration testable without a network.
/// </remarks>
public interface IBackupStorage
{
    /// <summary>The backups in the rotation, newest first. Never the pre-restore folder.</summary>
    Task<IReadOnlyList<BackupFile>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Uploads a backup. <paramref name="kind"/> decides which folder it lands in.</summary>
    Task UploadAsync(
        string name,
        Stream content,
        BackupKind kind,
        CancellationToken cancellationToken = default);

    /// <summary>Opens a backup for reading, or null when there is no such file.</summary>
    Task<Stream?> OpenReadAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Removes a backup from the rotation.</summary>
    Task DeleteAsync(string name, CancellationToken cancellationToken = default);
}

/// <summary>Taking a dump of the database, and putting one back.</summary>
public interface IDatabaseBackup
{
    /// <summary>
    /// Dumps the database, gzipped, to <paramref name="destination"/>.
    /// </summary>
    Task DumpAsync(Stream destination, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the database with the contents of <paramref name="gzippedDump"/>.
    /// </summary>
    /// <remarks>
    /// Destructive and not reversible by anything this class does — the caller is responsible for having
    /// taken the pre-restore backup first. It runs under a <i>different, privileged</i> credential from
    /// the rest of the application, because the application's own database user deliberately holds no DDL
    /// at all and could not drop a table if it tried.
    /// </remarks>
    Task RestoreAsync(Stream gzippedDump, CancellationToken cancellationToken = default);
}
