using System.Text.Json;
using Smartnet.Domain.Auditing;
using Smartnet.Infrastructure.Persistence;

namespace Smartnet.Infrastructure.Auditing;

/// <inheritdoc cref="IAuditWriter"/>
public sealed class AuditWriter : IAuditWriter
{
    private readonly SmartnetDbContext _db;
    private readonly IChangeContext _change;
    private readonly TimeProvider _time;

    public AuditWriter(SmartnetDbContext db, IChangeContext change, TimeProvider time)
    {
        _db = db;
        _change = change;
        _time = time;
    }

    public async Task RecordAsync(
        AuditAction action,
        string entityType,
        string entityId,
        long? userId = null,
        string? reason = null,
        object? details = null,
        CancellationToken cancellationToken = default)
    {
        _db.AuditLog.Add(new AuditLogEntry
        {
            CompanyId = _change.CompanyId,
            EntityType = entityType,
            EntityId = entityId,
            Action = action,

            // Fall back to the request's user, but let the caller override: a failed login knows
            // which account was targeted even though nobody is authenticated.
            ChangedBy = userId ?? _change.UserId,

            ChangedAt = _time.GetUtcNow().UtcDateTime,
            Reason = reason ?? _change.Reason,
            Changes = details is null ? null : JsonSerializer.Serialize(details),
            IpAddress = _change.IpAddress,
            UserAgent = _change.UserAgent,
            CorrelationId = _change.CorrelationId,
        });

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
