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
    /// <summary>
    /// How often the schedule asks whether a backup is due — not how often one is taken.
    /// </summary>
    /// <remarks>
    /// Fifteen minutes is the accuracy of the cadence and the cost of a restart, against four listings an
    /// hour on the remote store. Cheap, but not free: a listing is an FTP connection, and connecting too
    /// eagerly is what got this server banned once already. A minute would be four times the accuracy and
    /// sixty times the connections, for a job that runs hourly.
    /// </remarks>
    private static readonly TimeSpan CheckEvery = TimeSpan.FromMinutes(15);

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

        // The timer asks the question; BackupSchedule answers it. Deliberately NOT the backup interval.
        //
        // Ticking once per interval means the tick is anchored to process start, and every restart
        // re-arms it. That first took a backup after every redeploy (the tick fired immediately, so three
        // deploys made three backups). Gating those with an age check stopped the duplicates and
        // introduced the opposite failure: a restart re-arms the hour, so deploys spaced under an hour
        // apart skip every check in turn and no backup is ever taken. Four deploys inside seventy minutes
        // is an ordinary afternoon here.
        //
        // Checking often and taking rarely has neither problem. A restart costs at most one check, the
        // cadence stays true to the interval whatever the process does, and the age check is what decides
        // — the timer only decides how soon the schedule notices it is due.
        using var timer = new PeriodicTimer(CheckEvery, _time);

        // Settling time. It never prevented anything on its own — it only moved each dump two minutes
        // later — but the first seconds after boot are busy enough without a mysqldump.
        await Task.Delay(TimeSpan.FromMinutes(2), _time, stoppingToken).ConfigureAwait(false);

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunOnceAsync(interval, stoppingToken).ConfigureAwait(false);

            if (!await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                break;
            }
        }
    }

    private async Task RunOnceAsync(TimeSpan interval, CancellationToken stoppingToken)
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

            if (!BackupSchedule.IsDue(
                await NewestTakenUtcAsync(backups, stoppingToken).ConfigureAwait(false),
                _time.GetUtcNow().UtcDateTime,
                interval))
            {
                LogSkippedAsRecent(_logger);
                return;
            }

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

    /// <summary>
    /// The age of the newest backup on the store, or null when it cannot be established.
    /// </summary>
    /// <remarks>
    /// A store that will not answer must not be able to suspend the schedule. Null means "take one", so a
    /// listing failure costs a backup that may be redundant rather than the backup that mattered.
    /// </remarks>
    private static async Task<DateTime?> NewestTakenUtcAsync(
        IBackupService backups,
        CancellationToken stoppingToken)
    {
        try
        {
            var existing = await backups.ListAsync(stoppingToken).ConfigureAwait(false);

            return existing
                .Select(file => BackupNaming.TakenAtUtc(file.Name))
                .Where(taken => taken is not null)
                .DefaultIfEmpty(null)
                .Max();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Error,
        Message = "The scheduled backup failed. The next one will be attempted on the usual interval.")]
    private static partial void LogScheduledBackupFailed(ILogger logger, Exception exception);

    // Debug, not Information: now that the schedule checks four times an hour and takes one backup, "not
    // due yet" is the ordinary answer rather than a notable event. At Information it would be three lines
    // of noise an hour, which is how logs stop being read.
    [LoggerMessage(EventId = 2, Level = LogLevel.Debug, Message = "Not due for a backup yet.")]
    private static partial void LogSkippedAsRecent(ILogger logger);
}
