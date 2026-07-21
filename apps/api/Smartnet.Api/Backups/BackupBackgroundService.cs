using Microsoft.Extensions.Options;
using Smartnet.Domain.Backups;
using Smartnet.Infrastructure.Backups;

namespace Smartnet.Api.Backups;

/// <summary>
/// Takes a backup every hour, and keeps the newest fifteen.
/// </summary>
/// <remarks>
/// <para>
/// <b>It never throws.</b> A backup that fails is logged and the next hour is tried; the alternative is a
/// background service that dies on the first FTP timeout and takes no backups for a week without anybody
/// noticing. The failure is at Error level with the reason, so it is visible in the log rather than only
/// in the absence of files.
/// </para>
/// <para>
/// <b>Off unless configured and enabled.</b> A deployment with no destination — or one where an
/// administrator has switched the schedule off — does nothing at all rather than logging an hourly
/// complaint about credentials it was never given.
/// </para>
/// <para>
/// Single instance assumed, which is what this deployment runs. Two API containers would take two
/// backups an hour and race on the pruning; the fix then is a lease, not a longer timer.
/// </para>
/// </remarks>
public sealed partial class BackupBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly BackupOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<BackupBackgroundService> _logger;

    public BackupBackgroundService(
        IServiceScopeFactory scopes,
        IOptions<BackupOptions> options,
        TimeProvider time,
        ILogger<BackupBackgroundService> logger)
    {
        _scopes = scopes;
        _options = options.Value;
        _time = time;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromHours(_options.IntervalHours);

        // A first backup shortly after start rather than immediately: a restart storm should not each
        // time dump the database, and the API has better things to do in its first seconds.
        using var timer = new PeriodicTimer(interval, _time);

        await Task.Delay(TimeSpan.FromMinutes(2), _time, stoppingToken).ConfigureAwait(false);

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunOnceAsync(stoppingToken).ConfigureAwait(false);

            if (!await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                break;
            }
        }
    }

    private async Task RunOnceAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopes.CreateScope();

            var destinations = scope.ServiceProvider.GetRequiredService<IBackupDestinationProvider>();
            var destination = await destinations.CurrentAsync(stoppingToken).ConfigureAwait(false);

            if (destination is null || !destination.Enabled)
            {
                return;
            }

            var backups = scope.ServiceProvider.GetRequiredService<IBackupService>();
            await backups.BackupAsync(BackupKind.Scheduled, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down, not a failure.
        }
        catch (Exception ex)
        {
            LogScheduledBackupFailed(_logger, ex);
        }
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Error,
        Message = "The scheduled backup failed. The next one will be attempted on the usual interval.")]
    private static partial void LogScheduledBackupFailed(ILogger logger, Exception exception);
}
