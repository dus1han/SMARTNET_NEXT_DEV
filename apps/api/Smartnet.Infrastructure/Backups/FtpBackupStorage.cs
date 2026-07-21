using FluentFTP;
using Smartnet.Domain.Backups;

namespace Smartnet.Infrastructure.Backups;

/// <summary>
/// Backups on an FTP server, over TLS.
/// </summary>
/// <remarks>
/// <para>
/// <b>Explicit FTPS by default</b>: AUTH TLS on the normal port, which is what an existing FTP account
/// usually already supports. It matters more here than it looks — a dump of this database carries every
/// customer record, the <c>notes</c> table the legacy system uses as a credential store, and the
/// plaintext <c>password</c> column that lives until cutover. Plain FTP puts all of it, and the FTP
/// password, on the wire in the clear.
/// </para>
/// <para>
/// A connection per operation rather than a pooled client. Backups happen once an hour; a long-lived FTP
/// control connection would spend that hour going stale behind a NAT or an idle timeout, and the failure
/// would arrive at the moment of the backup rather than at the moment of the connect.
/// </para>
/// <para>
/// The destination is read from the database on every call — an administrator who has just corrected the
/// password should not have to wait for a redeploy, or wonder why the next backup still fails.
/// </para>
/// </remarks>
public sealed class FtpBackupStorage : IBackupStorage
{
    private readonly IBackupDestinationProvider _destinations;

    public FtpBackupStorage(IBackupDestinationProvider destinations) => _destinations = destinations;

    public async Task<IReadOnlyList<BackupFile>> ListAsync(CancellationToken cancellationToken = default)
    {
        var destination = await RequireAsync(cancellationToken).ConfigureAwait(false);
        await using var client = await ConnectAsync(destination, cancellationToken).ConfigureAwait(false);

        if (!await client.DirectoryExists(destination.RemotePath, cancellationToken).ConfigureAwait(false))
        {
            return [];
        }

        var listing = await client
            .GetListing(destination.RemotePath, cancellationToken)
            .ConfigureAwait(false);

        return [.. listing
            .Where(item => item.Type == FtpObjectType.File && BackupNaming.IsBackupName(item.Name))
            .Select(item => new BackupFile(item.Name, item.Size, item.Modified.ToUniversalTime()))
            .OrderByDescending(file => file.Name, StringComparer.Ordinal)];
    }

    public async Task UploadAsync(
        string name,
        Stream content,
        BackupKind kind,
        CancellationToken cancellationToken = default)
    {
        var destination = await RequireAsync(cancellationToken).ConfigureAwait(false);
        await using var client = await ConnectAsync(destination, cancellationToken).ConfigureAwait(false);

        var folder = kind == BackupKind.PreRestore ? destination.SafetyPath : destination.RemotePath;

        var status = await client
            .UploadStream(content, Remote(folder, name), FtpRemoteExists.Overwrite, createRemoteDir: true, token: cancellationToken)
            .ConfigureAwait(false);

        if (status != FtpStatus.Success)
        {
            throw new BackupFailedException($"Uploading '{name}' to {folder} did not succeed ({status}).");
        }
    }

    public async Task<Stream?> OpenReadAsync(string name, CancellationToken cancellationToken = default)
    {
        if (!BackupNaming.IsBackupName(name))
        {
            return null;
        }

        var destination = await RequireAsync(cancellationToken).ConfigureAwait(false);
        await using var client = await ConnectAsync(destination, cancellationToken).ConfigureAwait(false);

        // Downloaded whole, into memory, rather than handed back as a live FTP stream. A backup is about
        // 10 MB, and the alternative is a control connection whose lifetime is tied to whether the
        // browser finishes reading — which it does not, when somebody cancels the download.
        var buffer = new MemoryStream();

        // The rotation first, then the safety folder: a restore may legitimately name either.
        foreach (var folder in new[] { destination.RemotePath, destination.SafetyPath })
        {
            var path = Remote(folder, name);

            if (!await client.FileExists(path, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            if (!await client.DownloadStream(buffer, path, token: cancellationToken).ConfigureAwait(false))
            {
                throw new BackupFailedException($"Downloading '{name}' from {folder} did not succeed.");
            }

            buffer.Position = 0;
            return buffer;
        }

        await buffer.DisposeAsync().ConfigureAwait(false);
        return null;
    }

    public async Task DeleteAsync(string name, CancellationToken cancellationToken = default)
    {
        if (!BackupNaming.IsBackupName(name))
        {
            // Refused rather than sanitised. A name that is not one of ours is not a file we may delete.
            throw new ArgumentException($"'{name}' is not a backup name.", nameof(name));
        }

        var destination = await RequireAsync(cancellationToken).ConfigureAwait(false);
        await using var client = await ConnectAsync(destination, cancellationToken).ConfigureAwait(false);

        // Only ever from the rotation. Pre-restore copies are not this method's to remove.
        await client.DeleteFile(Remote(destination.RemotePath, name), cancellationToken).ConfigureAwait(false);
    }

    private async Task<BackupDestination> RequireAsync(CancellationToken cancellationToken) =>
        await _destinations.CurrentAsync(cancellationToken).ConfigureAwait(false)
        ?? throw new BackupNotConfiguredException();

    private static string Remote(string folder, string name) => $"{folder.TrimEnd('/')}/{name}";

    /// <summary>Opens a connection, or says plainly that the store cannot be reached.</summary>
    /// <remarks>
    /// <para>
    /// <b>Timeouts are set explicitly and kept short.</b> The defaults are generous and the retries
    /// multiply them, which is how a screen that lists backups came to hang for sixty seconds against an
    /// unreachable server and then return a 500. A remote store being down is an ordinary condition —
    /// the network is somebody else's — so it has to fail quickly and say what happened, not stall the
    /// page it is drawn on.
    /// </para>
    /// <para>
    /// The failure is translated here rather than left as a <c>TimeoutException</c> or a socket error, so
    /// callers get one thing to catch and the user gets a sentence instead of a stack trace.
    /// </para>
    /// </remarks>
    public static async Task<AsyncFtpClient> ConnectAsync(
        BackupDestination destination,
        CancellationToken cancellationToken)
    {
        var client = new AsyncFtpClient(
            destination.Host, destination.Username, destination.Password, destination.Port);

        client.Config.EncryptionMode = destination.UseTls ? FtpEncryptionMode.Explicit : FtpEncryptionMode.None;

        // Off unless asked for. Accepting any certificate would make a self-signed server "just work",
        // and would also make a machine-in-the-middle just work — see the setting's own remarks.
        client.Config.ValidateAnyCertificate = destination.AcceptAnyCertificate;

        client.Config.ConnectTimeout = ConnectTimeoutMs;
        client.Config.ReadTimeout = ReadTimeoutMs;
        client.Config.DataConnectionConnectTimeout = ConnectTimeoutMs;
        client.Config.DataConnectionReadTimeout = ReadTimeoutMs;

        // One attempt. Retrying a refused connection turns an eight-second failure into a half-minute one
        // and, against a host that throttles by source address, makes the next attempt likelier to fail
        // too — a connection per page load is already more than such a server expects.
        client.Config.RetryAttempts = 1;

        try
        {
            await client.Connect(cancellationToken).ConfigureAwait(false);
            return client;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await client.DisposeAsync().ConfigureAwait(false);

            throw new BackupStoreUnreachableException(
                $"Could not reach the backup server at {destination.Host}:{destination.Port}. "
                + "Check that the host, port and credentials are right and that the server is accepting "
                + "connections from this machine.",
                ex);
        }
    }

    private const int ConnectTimeoutMs = 8_000;
    private const int ReadTimeoutMs = 15_000;
}

/// <summary>
/// The backup destination is configured but cannot be talked to.
/// </summary>
/// <remarks>
/// Distinct from <see cref="BackupNotConfiguredException"/>, which means nobody has set one up. This one
/// means the settings exist and the server did not answer — a different sentence for the user, and a
/// different thing to go and check.
/// </remarks>
public sealed class BackupStoreUnreachableException(string message, Exception inner)
    : Exception(message, inner);

/// <summary>No backup destination has been configured on this deployment.</summary>
public sealed class BackupNotConfiguredException()
    : Exception("No backup destination is configured. Set one under Administration → Backups.");
