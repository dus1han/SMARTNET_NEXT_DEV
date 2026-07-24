using System.Formats.Tar;
using System.IO.Compression;

namespace Smartnet.Infrastructure.Backups;

/// <summary>
/// Snapshots the Data Protection key ring so it can travel with the database backup.
/// </summary>
/// <remarks>
/// The key ring is what decrypts the passwords the database only ever holds as ciphertext — the SMTP
/// password, the FTP backup password. A backup of the database <i>alone</i> restores to an application
/// that cannot send mail or reach its own backup server, because every stored password is unreadable
/// without the key that encrypted it. That failure has already happened once here: a key ring replaced
/// during a redeploy left the rows intact and the feature reporting an unreachable server, with the cause
/// three directories away. So the ring is backed up beside the data it unlocks.
/// </remarks>
public interface IKeyRingBackup
{
    /// <summary>Writes the key ring directory to <paramref name="destination"/> as a gzipped tar.</summary>
    Task SnapshotToAsync(Stream destination, CancellationToken cancellationToken = default);

    /// <summary>
    /// Unpacks a snapshot from <paramref name="archive"/> back into the key ring directory, merged with
    /// what is already there.
    /// </summary>
    /// <remarks>
    /// Merged, not replaced: a key file is named by its GUID and its contents never change, so a key that
    /// both the snapshot and the live ring hold is the same key, and one only the live ring holds is a
    /// newer key the snapshot predates. Keeping the union means a restore can only ever <i>add</i> the
    /// ability to decrypt something, never remove it — which is the entire point of restoring the ring.
    /// </remarks>
    Task RestoreFromAsync(Stream archive, CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IKeyRingBackup"/>
public sealed class KeyRingBackup : IKeyRingBackup
{
    private readonly string _keyRingPath;

    /// <param name="keyRingPath">
    /// The Data Protection key directory — the same <c>DataProtection:KeyPath</c> the application persists
    /// its keys to, and the same host directory the compose file mounts so it survives a redeploy.
    /// </param>
    public KeyRingBackup(string keyRingPath) => _keyRingPath = keyRingPath;

    public async Task SnapshotToAsync(Stream destination, CancellationToken cancellationToken = default)
    {
        // tar, then gzip on the way past. It is a handful of small XML files, so the compression is for a
        // tidy single artefact rather than for size. leaveOpen because the caller owns the stream it handed
        // in — the backup service writes it to a temp file it then re-opens to upload.
        await using var gzip = new GZipStream(destination, CompressionLevel.Optimal, leaveOpen: true);

        await TarFile
            .CreateFromDirectoryAsync(_keyRingPath, gzip, includeBaseDirectory: false, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task RestoreFromAsync(Stream archive, CancellationToken cancellationToken = default)
    {
        // The directory may not exist yet on a fresh box being restored into — this is exactly the
        // disaster-recovery case the snapshot is for.
        Directory.CreateDirectory(_keyRingPath);

        await using var gunzip = new GZipStream(archive, CompressionMode.Decompress);

        // overwriteFiles so re-restoring is idempotent (a key GUID's contents are immutable); files already
        // present that the archive does not carry are left untouched, so the result is the union of both.
        await TarFile
            .ExtractToDirectoryAsync(gunzip, _keyRingPath, overwriteFiles: true, cancellationToken)
            .ConfigureAwait(false);
    }
}
