namespace Smartnet.Domain.Auditing;

/// <summary>
/// Records the things that are not writes.
/// </summary>
/// <remarks>
/// The SaveChanges interceptor covers every mutation automatically. But auditing only mutations
/// misses the questions people actually ask — "who exported the customer list?", "was this
/// invoice ever emailed?", "who has been trying to log in as Chanaka?" — none of which change a
/// row. Those events are raised explicitly, through this.
/// </remarks>
public interface IAuditWriter
{
    /// <summary>
    /// Records a non-mutation event. <paramref name="userId"/> is passed explicitly because the
    /// caller is often not yet authenticated — a failed login is exactly the event worth having,
    /// and at that moment there is no user on the request.
    /// </summary>
    Task RecordAsync(
        AuditAction action,
        string entityType,
        string entityId,
        long? userId = null,
        string? reason = null,
        object? details = null,
        CancellationToken cancellationToken = default);
}
