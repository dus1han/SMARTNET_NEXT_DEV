namespace Smartnet.Domain.Backups;

/// <summary>
/// Whether the hourly job should actually take a backup this time round.
/// </summary>
/// <remarks>
/// <para>
/// The timer alone is not enough, because it is anchored to process start. Every redeploy restarted the
/// countdown and took a fresh backup two minutes later — three deploys in half an hour produced three
/// backups and the hourly tick was never once reached. Worse, each of those consumed one of the fifteen
/// slots in the rotation, so an active day of deployments would evict every genuine hourly backup and
/// leave fifteen copies of the same afternoon.
/// </para>
/// <para>
/// So the decision is made against what is already on the store rather than against how long this process
/// has been alive. Restarts stop mattering: whatever the timer does, a backup is taken only when the
/// newest one is old enough to warrant another.
/// </para>
/// </remarks>
public static class BackupSchedule
{
    /// <summary>
    /// How much early is close enough. A tick lands a few seconds either side of the interval, and a
    /// scheduler that skipped an hour over four seconds of drift would halve the backup rate.
    /// </summary>
    private static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(5);

    /// <param name="newestTakenUtc">
    /// When the newest backup on the store was taken, or null when there are none — or when the store
    /// could not be asked. Both mean "take one": a backup too many costs a few megabytes, and the
    /// alternative is a listing failure quietly suspending the schedule.
    /// </param>
    public static bool IsDue(DateTime? newestTakenUtc, DateTime utcNow, TimeSpan interval)
    {
        if (newestTakenUtc is null)
        {
            return true;
        }

        // A backup stamped in the future means somebody's clock is wrong. Taking one is the safe reading;
        // the other way round, a single bad stamp could suspend backups indefinitely.
        var age = utcNow - newestTakenUtc.Value;

        return age < TimeSpan.Zero || age >= interval - Tolerance;
    }
}
