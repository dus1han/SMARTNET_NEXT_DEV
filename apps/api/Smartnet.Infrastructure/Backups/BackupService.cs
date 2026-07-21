using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Smartnet.Domain.Backups;

namespace Smartnet.Infrastructure.Backups;

/// <summary>Taking backups, keeping the newest fifteen, and putting one back.</summary>
public interface IBackupService
{
    /// <summary>The rotation, newest first.</summary>
    Task<IReadOnlyList<BackupFile>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Dumps the database, uploads it, and prunes the rotation. Returns the name written.</summary>
    Task<string> BackupAsync(BackupKind kind, CancellationToken cancellationToken = default);

    /// <summary>Dumps the database to a stream without storing it — the "download now" path.</summary>
    Task<string> DumpToAsync(Stream destination, CancellationToken cancellationToken = default);

    /// <summary>Opens a stored backup, or null when there is no such file.</summary>
    Task<Stream?> OpenAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the database with a stored backup, after taking a safety copy of what is there now.
    /// </summary>
    Task<RestoreOutcome> RestoreAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the database with an uploaded file, after taking a safety copy of what is there now.
    /// </summary>
    Task<RestoreOutcome> RestoreAsync(Stream gzippedDump, CancellationToken cancellationToken = default);
}

/// <param name="SafetyBackup">The name of the pre-restore copy — the undo, if this was a mistake.</param>
public sealed record RestoreOutcome(string SafetyBackup);

/// <inheritdoc cref="IBackupService"/>
public sealed partial class BackupService : IBackupService
{
    private readonly IBackupStorage _storage;
    private readonly IDatabaseBackup _database;
    private readonly IBackupDestinationProvider _destinations;
    private readonly BackupOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<BackupService> _logger;

    public BackupService(
        IBackupStorage storage,
        IDatabaseBackup database,
        IBackupDestinationProvider destinations,
        IOptions<BackupOptions> options,
        TimeProvider time,
        ILogger<BackupService> logger)
    {
        _storage = storage;
        _database = database;
        _destinations = destinations;
        _options = options.Value;
        _time = time;
        _logger = logger;
    }

    public Task<IReadOnlyList<BackupFile>> ListAsync(CancellationToken cancellationToken = default) =>
        _storage.ListAsync(cancellationToken);

    public Task<Stream?> OpenAsync(string name, CancellationToken cancellationToken = default) =>
        _storage.OpenReadAsync(name, cancellationToken);

    public async Task<string> BackupAsync(BackupKind kind, CancellationToken cancellationToken = default)
    {
        var name = BackupNaming.For(kind, _time.GetUtcNow().UtcDateTime);

        // To a temp file, not memory. It is only ~10 MB today, but a backup process whose cost grows with
        // the database is one that starts failing on the day the database is big enough to matter.
        var scratch = Path.Combine(Path.GetTempPath(), name);

        try
        {
            await using (var file = File.Create(scratch))
            {
                await _database.DumpAsync(file, cancellationToken).ConfigureAwait(false);
            }

            await using (var upload = File.OpenRead(scratch))
            {
                await _storage.UploadAsync(name, upload, kind, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            TryDelete(scratch);
        }

        // Only the rotation is pruned. A pre-restore copy is deliberately outside it and outlives
        // everything — see BackupKind.PreRestore.
        if (kind != BackupKind.PreRestore)
        {
            await PruneAsync(cancellationToken).ConfigureAwait(false);
        }

        LogBackupTaken(_logger, name, kind);
        return name;
    }

    public async Task<string> DumpToAsync(Stream destination, CancellationToken cancellationToken = default)
    {
        var name = BackupNaming.For(BackupKind.Manual, _time.GetUtcNow().UtcDateTime);
        await _database.DumpAsync(destination, cancellationToken).ConfigureAwait(false);
        return name;
    }

    public async Task<RestoreOutcome> RestoreAsync(string name, CancellationToken cancellationToken = default)
    {
        var stored = await _storage.OpenReadAsync(name, cancellationToken).ConfigureAwait(false)
            ?? throw new BackupNotFoundException(name);

        await using (stored)
        {
            return await RestoreAsync(stored, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<RestoreOutcome> RestoreAsync(
        Stream gzippedDump,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.RestoreConnectionString))
        {
            // Checked here as well as in the runner, so the safety backup is not taken for a restore that
            // was never going to be possible.
            throw new RestoreUnavailableException();
        }

        // The undo, first. Everything after this point is destructive, and this is the only copy of what
        // is about to be overwritten — a restore of the wrong file is otherwise permanent.
        var safety = await BackupAsync(BackupKind.PreRestore, cancellationToken).ConfigureAwait(false);

        LogRestoreStarting(_logger, safety);

        await _database.RestoreAsync(gzippedDump, cancellationToken).ConfigureAwait(false);

        LogRestoreFinished(_logger, safety);

        return new RestoreOutcome(safety);
    }

    private async Task PruneAsync(CancellationToken cancellationToken)
    {
        var destination = await _destinations.CurrentAsync(cancellationToken).ConfigureAwait(false);

        if (destination is null)
        {
            return;
        }

        var existing = await _storage.ListAsync(cancellationToken).ConfigureAwait(false);

        foreach (var stale in BackupRetention.ToPrune(existing, destination.Retention))
        {
            await _storage.DeleteAsync(stale.Name, cancellationToken).ConfigureAwait(false);
            LogPruned(_logger, stale.Name);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Backup {Name} taken ({Kind})")]
    private static partial void LogBackupTaken(ILogger logger, string name, BackupKind kind);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Pruned old backup {Name}")]
    private static partial void LogPruned(ILogger logger, string name);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Warning,
        Message = "RESTORE starting. The database is about to be replaced; the copy taken first is {Safety}")]
    private static partial void LogRestoreStarting(ILogger logger, string safety);

    [LoggerMessage(EventId = 4, Level = LogLevel.Warning, Message = "RESTORE finished. Undo copy: {Safety}")]
    private static partial void LogRestoreFinished(ILogger logger, string safety);
}

/// <summary>The named backup is not on the store.</summary>
public sealed class BackupNotFoundException(string name)
    : Exception($"There is no backup named '{name}'.");
