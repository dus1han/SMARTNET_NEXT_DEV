using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using Microsoft.Extensions.Options;
using MySqlConnector;
using Smartnet.Domain.Backups;

namespace Smartnet.Infrastructure.Backups;

/// <summary>
/// Dump and restore, by driving the MariaDB client binaries.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a subprocess and not C#.</b> A correct logical dump is not a loop over tables — it is column
/// types, character sets, generated columns, foreign-key ordering, escaping of binary blobs, and the
/// exact <c>CREATE TABLE</c> the server would emit. <c>mysqldump</c> is the reference implementation of
/// that and ships with the server; re-implementing it here would be a second, worse one whose bugs
/// surface on the day the backup is needed. The binaries are in the API image — see apps/api/Dockerfile.
/// </para>
/// <para>
/// <b>The password never appears in the command line.</b> It goes in a defaults file, mode 0600, deleted
/// in a finally. Anything on the argument list is visible in <c>/proc</c> to every process in the
/// container for as long as the dump runs, which for a 30-megabyte database is long enough.
/// </para>
/// </remarks>
public sealed class MySqlDatabaseBackup : IDatabaseBackup
{
    private readonly BackupOptions _options;
    private readonly string _appConnectionString;

    public MySqlDatabaseBackup(IOptions<BackupOptions> options, IDatabaseConnectionString appConnection)
    {
        _options = options.Value;
        _appConnectionString = appConnection.Value;
    }

    public async Task DumpAsync(Stream destination, CancellationToken cancellationToken = default)
    {
        var settings = Parse(_appConnectionString);

        // --single-transaction: a consistent snapshot on InnoDB without locking anybody out. The
        //   alternative, --lock-tables, would freeze the application for the length of the dump.
        // --skip-lock-tables: explicit, because --single-transaction only suppresses locking for the
        //   dump's own tables and MariaDB still reaches for LOCK TABLES otherwise.
        // --no-tablespaces: the app user has no PROCESS privilege, and this is what asks for it.
        // No --events. The app user has no EVENT privilege, and there are no events in this schema, so
        //   the flag buys nothing and fails the whole dump with "Access denied ... to database".
        var arguments = new[]
        {
            "--single-transaction",
            "--skip-lock-tables",
            "--no-tablespaces",
            "--routines",
            "--triggers",
            "--default-character-set=utf8mb4",
            settings.Database,
        };

        await RunAsync(
            _options.DumpCommand,
            arguments,
            settings,
            async (process, token) =>
            {
                // Gzip on the way past. A 21 MB dump compresses to about 10 MB, and it is the difference
                // between fifteen backups costing 300 MB and costing 150 MB on somebody else's server.
                await using var gzip = new GZipStream(destination, CompressionLevel.Optimal, leaveOpen: true);
                await process.StandardOutput.BaseStream.CopyToAsync(gzip, token).ConfigureAwait(false);
            },
            redirectInput: false,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task RestoreAsync(Stream gzippedDump, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.RestoreConnectionString))
        {
            throw new RestoreUnavailableException();
        }

        var settings = Parse(_options.RestoreConnectionString);

        await RunAsync(
            _options.RestoreCommand,
            ["--default-character-set=utf8mb4", settings.Database],
            settings,
            async (process, token) =>
            {
                await using var gunzip = new GZipStream(gzippedDump, CompressionMode.Decompress, leaveOpen: true);
                await gunzip.CopyToAsync(process.StandardInput.BaseStream, token).ConfigureAwait(false);
                await process.StandardInput.BaseStream.FlushAsync(token).ConfigureAwait(false);
                process.StandardInput.Close();
            },
            redirectInput: true,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Runs a client binary with credentials supplied out of band, and fails loudly.</summary>
    private async Task RunAsync(
        string command,
        string[] arguments,
        MySqlConnectionStringBuilder settings,
        Func<Process, CancellationToken, Task> pump,
        bool redirectInput,
        CancellationToken cancellationToken)
    {
        var defaultsFile = await WriteDefaultsFileAsync(settings, cancellationToken).ConfigureAwait(false);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(_options.TimeoutMinutes));

        try
        {
            var start = new ProcessStartInfo
            {
                FileName = command,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = redirectInput,
                UseShellExecute = false,
            };

            // First argument, as MariaDB requires: --defaults-extra-file is only honoured before the rest.
            start.ArgumentList.Add($"--defaults-extra-file={defaultsFile}");

            foreach (var argument in arguments)
            {
                start.ArgumentList.Add(argument);
            }

            using var process = Process.Start(start)
                ?? throw new InvalidOperationException($"'{command}' did not start.");

            // stderr is read concurrently. mysqldump writes warnings there while streaming to stdout, and
            // a full pipe buffer with nobody reading it deadlocks the process against us.
            var errors = process.StandardError.ReadToEndAsync(timeout.Token);

            await pump(process, timeout.Token).ConfigureAwait(false);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);

            var stderr = await errors.ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                throw new BackupFailedException(
                    $"{command} exited with {process.ExitCode.ToString(CultureInfo.InvariantCulture)}: "
                    + Redact(stderr, settings));
            }
        }
        finally
        {
            TryDelete(defaultsFile);
        }
    }

    /// <summary>
    /// The credentials, in a file only this process can read, rather than on the command line.
    /// </summary>
    private static async Task<string> WriteDefaultsFileAsync(
        MySqlConnectionStringBuilder settings,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(Path.GetTempPath(), $"smartnet-backup-{Guid.NewGuid():N}.cnf");

        // Created empty and locked down BEFORE the secret goes in: writing first and chmod-ing after
        // leaves a window in which the password sits in a world-readable file.
        using (var handle = File.Create(path))
        {
            _ = handle;
        }

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        var contents = string.Create(
            CultureInfo.InvariantCulture,
            $"""
            [client]
            host={settings.Server}
            port={settings.Port}
            user={settings.UserID}
            password="{settings.Password.Replace("\"", "\\\"", StringComparison.Ordinal)}"

            """);

        await File.WriteAllTextAsync(path, contents, cancellationToken).ConfigureAwait(false);

        return path;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // A leftover defaults file is untidy; failing the backup over it would be worse.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Keeps the password out of an error message that will be logged and shown.</summary>
    private static string Redact(string text, MySqlConnectionStringBuilder settings) =>
        string.IsNullOrEmpty(settings.Password)
            ? text
            : text.Replace(settings.Password, "***", StringComparison.Ordinal);

    private static MySqlConnectionStringBuilder Parse(string connectionString) => new(connectionString);
}

/// <summary>The application's own connection string, so the dump reads what the app reads.</summary>
public interface IDatabaseConnectionString
{
    string Value { get; }
}

/// <inheritdoc />
public sealed class DatabaseConnectionString(string value) : IDatabaseConnectionString
{
    public string Value { get; } = value;
}

/// <summary>A dump or a restore did not complete.</summary>
public sealed class BackupFailedException(string message) : Exception(message);

/// <summary>
/// Restore was asked for on a deployment that has no privileged credential configured.
/// </summary>
/// <remarks>
/// Not an error so much as a configuration answer: the application's own database user holds no DDL, by
/// design, so without <c>Backup:RestoreConnectionString</c> there is nothing that could perform a
/// restore. Backups, downloads and the rotation are unaffected.
/// </remarks>
public sealed class RestoreUnavailableException()
    : Exception(
        "Restore is not configured on this deployment. It needs a database credential that may drop and "
        + "recreate the schema, which the application's own user deliberately does not have.");
