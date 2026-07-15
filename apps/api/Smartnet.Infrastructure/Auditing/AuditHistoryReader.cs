using Microsoft.EntityFrameworkCore;
using Smartnet.Domain.Auditing;
using Smartnet.Domain.Identity;
using Smartnet.Infrastructure.Persistence;

namespace Smartnet.Infrastructure.Auditing;

/// <inheritdoc cref="IAuditHistory"/>
public sealed class AuditHistoryReader : IAuditHistory
{
    private readonly SmartnetDbContext _db;

    public AuditHistoryReader(SmartnetDbContext db) => _db = db;

    /// <summary>
    /// Derived from the EF model rather than a hand-kept list, because the interceptor writes
    /// <c>audit_log.entity_type</c> from exactly this: the CLR name of any entity implementing
    /// <see cref="IAuditable"/>. A list maintained by hand is a list that is one Phase-5 entity out
    /// of date.
    /// </summary>
    public bool IsAuditableEntity(string entityType) => _db.Model
        .GetEntityTypes()
        .Any(type =>
            typeof(IAuditable).IsAssignableFrom(type.ClrType)
            && string.Equals(type.ClrType.Name, entityType, StringComparison.Ordinal));

    public async Task<RecordHistory> ForRecordAsync(
        string entityType,
        string entityId,
        HistoryScope scope,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var query = Visible(_db.AuditLog, scope)
            .Where(e => e.EntityType == entityType && e.EntityId == entityId);

        var total = await query.CountAsync(cancellationToken).ConfigureAwait(false);

        var rows = await query
            // Newest first, then by id: two changes saved inside the same transaction share a
            // timestamp to the second, and "the order they happened in" is then the insert order.
            .OrderByDescending(e => e.ChangedAt)
            .ThenByDescending(e => e.Id)
            .Take(HistoryLimits.Clamp(limit))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var names = await NamesOf(rows.Select(r => r.ChangedBy), cancellationToken)
            .ConfigureAwait(false);

        var events = rows.Select(row => new HistoryEvent(
            row.Id,
            row.EntityType,
            row.EntityId,
            row.Action,
            row.ChangedBy,
            Name(names, row.ChangedBy),
            row.ChangedAt,
            row.Reason,
            row.Changes,
            row.IpAddress,
            row.CorrelationId)).ToList();

        return new RecordHistory(events, total);
    }

    public async Task<RecordHistory> BrowseAsync(
        AuditLogFilter filter,
        HistoryScope scope,
        CancellationToken cancellationToken = default)
    {
        var query = Visible(_db.AuditLog, scope);

        // Each filter narrows only when set — an absent one is not "match nothing", it is "do not
        // ask". The bounds are half-open (>= From, < To) so the controller's "to the 14th" maps to
        // "before the 15th" without an off-by-a-day at midnight.
        if (filter.From is { } from)
        {
            query = query.Where(e => e.ChangedAt >= from);
        }

        if (filter.To is { } to)
        {
            query = query.Where(e => e.ChangedAt < to);
        }

        if (filter.UserId is { } userId)
        {
            query = query.Where(e => e.ChangedBy == userId);
        }

        if (filter.Action is { } action)
        {
            query = query.Where(e => e.Action == action);
        }

        if (!string.IsNullOrEmpty(filter.EntityType))
        {
            query = query.Where(e => e.EntityType == filter.EntityType);
        }

        var total = await query.CountAsync(cancellationToken).ConfigureAwait(false);

        var rows = await query
            .OrderByDescending(e => e.ChangedAt)
            .ThenByDescending(e => e.Id)
            .Take(HistoryLimits.Clamp(filter.Limit))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var names = await NamesOf(rows.Select(r => r.ChangedBy), cancellationToken)
            .ConfigureAwait(false);

        var events = rows.Select(row => new HistoryEvent(
            row.Id,
            row.EntityType,
            row.EntityId,
            row.Action,
            row.ChangedBy,
            Name(names, row.ChangedBy),
            row.ChangedAt,
            row.Reason,
            row.Changes,
            row.IpAddress,
            row.CorrelationId)).ToList();

        return new RecordHistory(events, total);
    }

    public async Task<AuditLogFacets> FacetsAsync(
        HistoryScope scope,
        CancellationToken cancellationToken = default)
    {
        var visible = Visible(_db.AuditLog, scope);

        var entityTypes = await visible
            .Select(e => e.EntityType)
            .Distinct()
            .OrderBy(type => type)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var actorIds = await visible
            .Where(e => e.ChangedBy != null)
            .Select(e => e.ChangedBy!.Value)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var names = await NamesOf(actorIds.Cast<long?>(), cancellationToken).ConfigureAwait(false);

        // A user who has since been removed still appears — their id is in the log and their actions
        // still have to be filterable to. "User 14" is the honest label when the name is gone.
        var actors = actorIds
            .Select(id => new AuditActor(
                id,
                names.TryGetValue(id, out var name) ? name : $"User {id}"))
            .OrderBy(actor => actor.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return new AuditLogFacets(entityTypes, actors);
    }

    public async Task<IReadOnlyList<DocumentVersionInfo>> VersionsAsync(
        string docType,
        long docId,
        HistoryScope scope,
        CancellationToken cancellationToken = default)
    {
        var rows = await Visible(_db.DocumentVersions, scope)
            .Where(v => v.DocType == docType && v.DocId == docId)
            .OrderByDescending(v => v.VersionNo)
            // The snapshot is the whole document. Loading nine of them to draw a list of nine dates
            // is the difference between a tab that opens and one that thinks about it.
            .Select(v => new
            {
                v.Id, v.DocType, v.DocId, v.VersionNo, v.ChangedBy, v.ChangedAt, v.Reason,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var names = await NamesOf(rows.Select(r => r.ChangedBy), cancellationToken)
            .ConfigureAwait(false);

        return rows.Select(row => new DocumentVersionInfo(
            row.Id,
            row.DocType,
            row.DocId,
            row.VersionNo,
            row.ChangedBy,
            Name(names, row.ChangedBy),
            row.ChangedAt,
            row.Reason,
            Snapshot: null)).ToList();
    }

    public async Task<DocumentVersionInfo?> VersionAsync(
        string docType,
        long docId,
        int versionNo,
        HistoryScope scope,
        CancellationToken cancellationToken = default)
    {
        var row = await Visible(_db.DocumentVersions, scope)
            .FirstOrDefaultAsync(
                v => v.DocType == docType && v.DocId == docId && v.VersionNo == versionNo,
                cancellationToken)
            .ConfigureAwait(false);

        if (row is null)
        {
            return null;
        }

        var names = await NamesOf([row.ChangedBy], cancellationToken).ConfigureAwait(false);

        return new DocumentVersionInfo(
            row.Id,
            row.DocType,
            row.DocId,
            row.VersionNo,
            row.ChangedBy,
            Name(names, row.ChangedBy),
            row.ChangedAt,
            row.Reason,
            row.Snapshot);
    }

    /// <summary>
    /// The company scoping. Both history tables go through it; neither is read without it.
    /// </summary>
    /// <remarks>
    /// The scope is the set of companies in the caller's token — which today, for every user in this
    /// business, is <i>all</i> of them: Smart Net and Smart Technologies are two trading entities,
    /// not two tenants (see <c>ICompanyAccessService</c>). So this filter is currently a no-op, and
    /// it is here anyway: the day an entity is acquired whose books the existing staff should not
    /// see, the boundary has to already be on the read path, not be retrofitted onto forty screens.
    /// <para>
    /// The audit log deliberately has no <i>global</i> query filter on it — the interceptor must be
    /// able to write a row for any company, and a filter the writer has to remember to bypass is one
    /// that will eventually swallow an audit row. So the scoping lives here, on the read side, once.
    /// </para>
    /// <para>
    /// Rows with no company are visible to anyone who may read history at all — see
    /// <see cref="HistoryScope"/> for why a login is one of them.
    /// </para>
    /// </remarks>
    private static IQueryable<AuditLogEntry> Visible(IQueryable<AuditLogEntry> log, HistoryScope scope)
    {
        // Materialised: EF translates Contains over a local collection, not over a set interface.
        var companies = scope.Companies.ToList();

        return log.Where(e => e.CompanyId == null || companies.Contains(e.CompanyId.Value));
    }

    /// <inheritdoc cref="Visible(IQueryable{AuditLogEntry}, HistoryScope)"/>
    private static IQueryable<DocumentVersion> Visible(
        IQueryable<DocumentVersion> versions,
        HistoryScope scope)
    {
        var companies = scope.Companies.ToList();

        return versions.Where(v => v.CompanyId == null || companies.Contains(v.CompanyId.Value));
    }

    /// <summary>
    /// Resolves the ids to display names in one round trip, rather than one per row.
    /// </summary>
    /// <remarks>
    /// Disabled users are resolved too: nothing is hard-deleted, and their history has to stay
    /// attributable to a name rather than turning into "user 14" the day they leave.
    /// </remarks>
    private async Task<Dictionary<long, string>> NamesOf(
        IEnumerable<long?> userIds,
        CancellationToken cancellationToken)
    {
        var ids = userIds.Where(id => id is not null).Select(id => id!.Value).Distinct().ToList();

        if (ids.Count == 0)
        {
            return [];
        }

        return await _db.Users
            .Where(user => ids.Contains(user.Id))
            .ToDictionaryAsync(
                user => user.Id,
                user => user.Name ?? user.Username ?? string.Empty,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Null when the actor is unknown (an anonymous event) or no longer exists.</summary>
    private static string? Name(Dictionary<long, string> names, long? userId) =>
        userId is not null && names.TryGetValue(userId.Value, out var name) ? name : null;
}
