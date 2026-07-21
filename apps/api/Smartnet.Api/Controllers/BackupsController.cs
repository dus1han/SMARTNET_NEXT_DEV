using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Smartnet.Api.Auditing;
using Smartnet.Api.Auth;
using Smartnet.Api.Contracts;
using Smartnet.Domain.Backups;
using Smartnet.Domain.Identity;
using Smartnet.Infrastructure.Backups;
using Smartnet.Infrastructure.Persistence;

namespace Smartnet.Api.Controllers;

/// <summary>
/// Database backups — the schedule, the manual button, and the restore.
/// </summary>
/// <remarks>
/// <para>
/// <b>Dev_Admin throughout.</b> A backup is a copy of every record the business has, including the
/// plaintext <c>password</c> column that lives until cutover and the <c>notes</c> table the legacy system
/// uses as a credential store. Downloading one is exfiltrating the company; restoring one is overwriting
/// it. Neither is an administrative task, and neither is grantable piecemeal.
/// </para>
/// <para>
/// <b>What a restore costs, stated plainly.</b> It replaces every table, <c>audit_log</c> included — the
/// table that was deliberately made append-only so that the application could not rewrite history. That
/// is inherent to restoring a database rather than a flaw here, but it means a restore is the one action
/// in this system that can erase the record of itself. It therefore takes a safety copy first, demands a
/// typed confirmation and a change reason, and logs at Warning either side.
/// </para>
/// </remarks>
[ApiController]
[Route("api/backups")]
[RequirePermission(Permissions.SystemDevAdmin)]
public sealed class BackupsController : ControllerBase
{
    private readonly IBackupService _backups;
    private readonly SmartnetDbContext _db;
    private readonly BackupOptions _options;
    private readonly IDataProtector _protector;

    public BackupsController(
        IBackupService backups,
        SmartnetDbContext db,
        IOptions<BackupOptions> options,
        IDataProtectionProvider protection)
    {
        _backups = backups;
        _db = db;
        _options = options.Value;
        _protector = protection.CreateProtector(BackupDestinationProvider.ProtectorPurpose);
    }

    private bool RestoreAvailable => !string.IsNullOrWhiteSpace(_options.RestoreConnectionString);

    // --- Destination -------------------------------------------------------------------------

    [HttpGet("settings")]
    public async Task<ActionResult<BackupSettingsResponse>> Settings(CancellationToken cancellationToken)
    {
        var settings = await Current(cancellationToken).ConfigureAwait(false);

        return Ok(new BackupSettingsResponse(
            settings?.Enabled ?? false,
            settings?.Host ?? string.Empty,
            settings?.Port ?? 21,
            settings?.Username,
            HasPassword: !string.IsNullOrEmpty(settings?.PasswordEncrypted),
            settings?.UseTls ?? true,
            settings?.AcceptAnyCertificate ?? false,
            settings?.RemotePath ?? "/",
            settings?.SafetyPath ?? "/pre-restore",
            settings?.Retention ?? 15,
            RestoreAvailable));
    }

    [HttpPut("settings")]
    [RequireChangeReason]
    public async Task<IActionResult> SaveSettings(
        SaveBackupSettingsRequest request,
        CancellationToken cancellationToken)
    {
        var settings = await Current(cancellationToken).ConfigureAwait(false);

        if (settings is null)
        {
            settings = new BackupSettings();
            _db.BackupSettings.Add(settings);
        }

        settings.Enabled = request.Enabled;
        settings.Host = request.Host.Trim();
        settings.Port = request.Port;
        settings.Username = request.Username?.Trim();
        settings.UseTls = request.UseTls;
        settings.AcceptAnyCertificate = request.AcceptAnyCertificate;
        settings.RemotePath = request.RemotePath.Trim();
        settings.SafetyPath = request.SafetyPath.Trim();
        settings.Retention = request.Retention;

        // Null means "leave it alone" — what the screen sends when the port changed and the password did
        // not. Only a supplied value replaces it, and it is encrypted before it touches the row.
        if (!string.IsNullOrEmpty(request.Password))
        {
            settings.PasswordEncrypted = _protector.Protect(request.Password);
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return NoContent();
    }

    // --- The rotation ------------------------------------------------------------------------

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<BackupSummary>>> List(CancellationToken cancellationToken)
    {
        try
        {
            var files = await _backups.ListAsync(cancellationToken).ConfigureAwait(false);

            return Ok(files
                .Select(f => new BackupSummary(f.Name, f.SizeBytes, f.ModifiedUtc))
                .ToList());
        }
        catch (BackupNotConfiguredException)
        {
            // Not an error: nothing has been set up yet. An empty list plus the settings form is a more
            // useful answer than a 500 the screen has to interpret.
            return Ok(Array.Empty<BackupSummary>());
        }
    }

    /// <summary>Takes a backup now and stores it on the destination.</summary>
    [HttpPost]
    [RequireChangeReason]
    public async Task<ActionResult<BackupTakenResponse>> TakeNow(CancellationToken cancellationToken)
    {
        try
        {
            var name = await _backups.BackupAsync(BackupKind.Manual, cancellationToken).ConfigureAwait(false);
            return Ok(new BackupTakenResponse(name));
        }
        catch (BackupNotConfiguredException notConfigured)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: notConfigured.Message);
        }
        catch (BackupFailedException failed)
        {
            return Problem(statusCode: StatusCodes.Status502BadGateway, title: failed.Message);
        }
    }

    /// <summary>
    /// Takes a backup now and streams it to the browser, storing nothing.
    /// </summary>
    /// <remarks>
    /// The path that works when the FTP destination is broken or absent — which is exactly when somebody
    /// most wants a copy of the database in their hand.
    /// </remarks>
    [HttpGet("download")]
    public async Task<IActionResult> DownloadFreshBackup(CancellationToken cancellationToken)
    {
        var scratch = Path.GetTempFileName();

        try
        {
            string name;

            await using (var file = System.IO.File.Create(scratch))
            {
                name = await _backups.DumpToAsync(file, cancellationToken).ConfigureAwait(false);
            }

            // Read back from disk rather than buffering the dump in memory, and let the framework delete
            // the file once the response has been written.
            var contents = System.IO.File.OpenRead(scratch);
            Response.RegisterForDispose(new TempFile(scratch));

            return File(contents, "application/gzip", name);
        }
        catch (BackupFailedException failed)
        {
            TryDelete(scratch);
            return Problem(statusCode: StatusCodes.Status502BadGateway, title: failed.Message);
        }
    }

    /// <summary>Streams a stored backup back.</summary>
    [HttpGet("{name}/download")]
    public async Task<IActionResult> Download(string name, CancellationToken cancellationToken)
    {
        if (!BackupNaming.IsBackupName(name))
        {
            return NotFound();
        }

        var stream = await _backups.OpenAsync(name, cancellationToken).ConfigureAwait(false);

        return stream is null ? NotFound() : File(stream, "application/gzip", name);
    }

    // --- Restore -----------------------------------------------------------------------------

    /// <summary>Replaces the database with a stored backup.</summary>
    [HttpPost("{name}/restore")]
    [RequireChangeReason]
    public async Task<ActionResult<RestoreCompletedResponse>> Restore(
        string name,
        RestoreRequest request,
        CancellationToken cancellationToken)
    {
        _ = request; // validated by RestoreRequestValidator — the typed confirmation

        if (!BackupNaming.IsBackupName(name))
        {
            return NotFound();
        }

        return await RunRestore(
            token => _backups.RestoreAsync(name, token),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Replaces the database with an uploaded backup file.</summary>
    /// <remarks>
    /// The file is executed as SQL against the database by the MariaDB client. There is no meaningful way
    /// to validate that a dump is "safe" short of running it, so this endpoint's protection is entirely
    /// that only a Dev_Admin can reach it — which is the same trust already required to restore from the
    /// store, and the reason both sit behind the same permission.
    /// </remarks>
    [HttpPost("restore")]
    [RequireChangeReason]
    [RequestSizeLimit(2L * 1024 * 1024 * 1024)]
    public async Task<ActionResult<RestoreCompletedResponse>> RestoreUpload(
        IFormFile file,
        [FromForm] string confirm,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(confirm, "RESTORE", StringComparison.Ordinal))
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Type RESTORE to confirm that every record in the database will be replaced.");
        }

        if (file is null || file.Length == 0)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "No file was uploaded.");
        }

        if (!file.FileName.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "A backup is a gzipped SQL dump (.sql.gz).");
        }

        await using var upload = file.OpenReadStream();

        return await RunRestore(
            token => _backups.RestoreAsync(upload, token),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<ActionResult<RestoreCompletedResponse>> RunRestore(
        Func<CancellationToken, Task<RestoreOutcome>> restore,
        CancellationToken cancellationToken)
    {
        try
        {
            var outcome = await restore(cancellationToken).ConfigureAwait(false);
            return Ok(new RestoreCompletedResponse(outcome.SafetyBackup));
        }
        catch (RestoreUnavailableException unavailable)
        {
            // 501: the deployment cannot do this, and no amount of retrying will change that.
            return Problem(statusCode: StatusCodes.Status501NotImplemented, title: unavailable.Message);
        }
        catch (BackupNotConfiguredException notConfigured)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: notConfigured.Message);
        }
        catch (BackupNotFoundException notFound)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound, title: notFound.Message);
        }
        catch (BackupFailedException failed)
        {
            // The safety copy was taken before anything was touched, so its name is in the log even when
            // the restore itself died half way.
            return Problem(statusCode: StatusCodes.Status502BadGateway, title: failed.Message);
        }
    }

    private Task<BackupSettings?> Current(CancellationToken cancellationToken) =>
        _db.BackupSettings.FirstOrDefaultAsync(cancellationToken);

    private static void TryDelete(string path)
    {
        try
        {
            System.IO.File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Deletes a scratch file once the response has been written.</summary>
    private sealed class TempFile(string path) : IDisposable
    {
        public void Dispose() => TryDelete(path);
    }
}
