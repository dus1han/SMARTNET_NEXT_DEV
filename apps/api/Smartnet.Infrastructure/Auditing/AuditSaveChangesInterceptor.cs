using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Smartnet.Domain.Auditing;

namespace Smartnet.Infrastructure.Auditing;

/// <summary>
/// Stamps the audit columns and writes <c>audit_log</c> automatically on every save.
/// </summary>
/// <remarks>
/// This is the whole point of AUDIT.md: a developer cannot forget to audit a change, because
/// there is no audit code for them to write. Add an entity in a later phase and it is audited
/// the moment it is saved.
///
/// <para><b>Why this runs in two phases.</b> An inserted row's primary key does not exist until
/// the INSERT has run, so <c>audit_log.entity_id</c> cannot be known in
/// <see cref="SavingChangesAsync"/>. So we diff before the save (while the change tracker still
/// holds the original values, which it discards afterwards) and write the rows after it, once
/// the keys are real.</para>
///
/// <para><b>Why that is still one transaction.</b> The second write would be a second
/// transaction — audit and data could diverge, which is exactly what the spec forbids. So
/// <see cref="Persistence.SmartnetDbContext.SaveChangesAsync"/> opens a transaction around the
/// whole thing when the caller has not already opened one. Both writes commit, or neither does.
/// Removing that override silently breaks this guarantee, which is why it is not optional.</para>
///
/// <para>Registered <b>scoped</b>, matching the DbContext, because it holds per-save state.</para>
/// </remarks>
public sealed class AuditSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly IChangeContext _change;
    private readonly TimeProvider _time;

    /// <summary>Diffs captured before the save, awaiting their keys.</summary>
    private readonly List<PendingAudit> _pending = [];

    /// <summary>Guards the re-entrant save that writes the audit rows themselves.</summary>
    private bool _writingAuditRows;

    public AuditSaveChangesInterceptor(IChangeContext change, TimeProvider time)
    {
        _change = change;
        _time = time;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null && !_writingAuditRows)
        {
            Capture(eventData.Context);
        }

        return ValueTask.FromResult(result);
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is null || _writingAuditRows || _pending.Count == 0)
        {
            return result;
        }

        var context = eventData.Context;
        var rows = _pending.Select(p => p.ToEntry()).ToList();
        _pending.Clear();

        _writingAuditRows = true;
        try
        {
            context.Set<AuditLogEntry>().AddRange(rows);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writingAuditRows = false;
        }

        return result;
    }

    /// <summary>The change tracker discards original values after a save, so we must diff first.</summary>
    private void Capture(DbContext context)
    {
        _pending.Clear();

        var now = _time.GetUtcNow().UtcDateTime;

        // ToList: soft-delete rewrites entry states, which mutates the change tracker.
        var entries = context.ChangeTracker.Entries()
            .Where(e => e.Entity is IAuditable)
            .ToList();

        foreach (var entry in entries)
        {
            var auditable = (IAuditable)entry.Entity;
            var action = Classify(entry, auditable);

            if (action is null)
            {
                continue;
            }

            switch (action)
            {
                case AuditAction.Create:
                    auditable.CreatedBy = _change.UserId;
                    auditable.CreatedAt = now;
                    auditable.RowVersion = 1;
                    break;

                case AuditAction.Delete:
                    auditable.DeletedBy = _change.UserId;
                    auditable.DeletedAt = now;
                    // Nothing is hard-deleted. Rewrite the delete as an update so the row
                    // survives and stays attributable.
                    entry.State = EntityState.Modified;
                    goto case AuditAction.Update;

                case AuditAction.Restore:
                case AuditAction.Update:
                    auditable.UpdatedBy = _change.UserId;
                    auditable.UpdatedAt = now;
                    auditable.RowVersion++;
                    break;
            }

            _pending.Add(new PendingAudit(
                Entry: entry,
                Action: action.Value,
                Changes: Diff(entry, action.Value),
                Context: _change,
                At: now));
        }
    }

    /// <summary>Null means "nothing worth auditing" — e.g. a Modified entry whose values all match.</summary>
    private static AuditAction? Classify(EntityEntry entry, IAuditable auditable) => entry.State switch
    {
        EntityState.Added => AuditAction.Create,
        EntityState.Deleted => AuditAction.Delete,
        EntityState.Modified when WasRestored(entry) => AuditAction.Restore,
        EntityState.Modified when auditable.DeletedAt is not null && WasDeleted(entry) => AuditAction.Delete,
        EntityState.Modified when entry.Properties.Any(IsRealChange) => AuditAction.Update,
        _ => null,
    };

    /// <summary>DeletedAt went from set to null: the row came back.</summary>
    private static bool WasRestored(EntityEntry entry)
    {
        var deletedAt = entry.Property(nameof(IAuditable.DeletedAt));
        return deletedAt.OriginalValue is not null && deletedAt.CurrentValue is null;
    }

    /// <summary>DeletedAt went from null to set: a soft delete applied by hand rather than by Remove().</summary>
    private static bool WasDeleted(EntityEntry entry)
    {
        var deletedAt = entry.Property(nameof(IAuditable.DeletedAt));
        return deletedAt.OriginalValue is null && deletedAt.CurrentValue is not null;
    }

    /// <summary>
    /// EF marks a property Modified when it is <i>assigned</i>, not when the value actually
    /// differs. Auditing the former produces a log full of no-op "changes", which is how an
    /// audit trail becomes noise nobody reads.
    /// </summary>
    private static bool IsRealChange(PropertyEntry p) =>
        p.IsModified && !Equals(p.OriginalValue, p.CurrentValue);

    /// <summary>Only the fields that changed — never the whole row.</summary>
    private static string? Diff(EntityEntry entry, AuditAction action)
    {
        var changes = new Dictionary<string, object?>();

        foreach (var p in entry.Properties)
        {
            var name = p.Metadata.Name;

            // The audit columns are the frame, not the picture. Logging "updated_at changed"
            // on every single update is noise.
            if (IsAuditColumn(name))
            {
                continue;
            }

            var include = action == AuditAction.Create
                ? p.CurrentValue is not null
                : IsRealChange(p);

            if (!include)
            {
                continue;
            }

            // Recorded as changed, never with its value.
            if (AuditRedaction.IsRedacted(name))
            {
                changes[name] = action == AuditAction.Create
                    ? new { to = AuditRedaction.Placeholder }
                    : (object)new { from = AuditRedaction.Placeholder, to = AuditRedaction.Placeholder };
                continue;
            }

            changes[name] = action == AuditAction.Create
                ? new { to = p.CurrentValue }
                : (object)new { from = p.OriginalValue, to = p.CurrentValue };
        }

        return changes.Count == 0 ? null : JsonSerializer.Serialize(changes);
    }

    private static bool IsAuditColumn(string name) => name
        is nameof(IAuditable.CreatedBy) or nameof(IAuditable.CreatedAt)
        or nameof(IAuditable.UpdatedBy) or nameof(IAuditable.UpdatedAt)
        or nameof(IAuditable.DeletedBy) or nameof(IAuditable.DeletedAt)
        or nameof(IAuditable.RowVersion);

    /// <summary>A diff waiting for its entity's key, which does not exist until the INSERT has run.</summary>
    private sealed record PendingAudit(
        EntityEntry Entry,
        AuditAction Action,
        string? Changes,
        IChangeContext Context,
        DateTime At)
    {
        public AuditLogEntry ToEntry() => new()
        {
            CompanyId = Context.CompanyId,
            EntityType = Entry.Metadata.ClrType.Name,
            EntityId = KeyOf(Entry),
            Action = Action,
            ChangedBy = Context.UserId,
            ChangedAt = At,
            Reason = Context.Reason,
            Changes = Changes,
            IpAddress = Context.IpAddress,
            UserAgent = Context.UserAgent,
            CorrelationId = Context.CorrelationId,
        };

        /// <summary>Composite keys join with '|'; the schema has none today, but it will.</summary>
        private static string KeyOf(EntityEntry entry)
        {
            var key = entry.Metadata.FindPrimaryKey()
                ?? throw new InvalidOperationException(
                    $"{entry.Metadata.ClrType.Name} is auditable but has no primary key. " +
                    "An audit row that cannot name the row it describes is useless. " +
                    "Give the entity a key before making it IAuditable.");

            // Invariant: what a row is called in the audit log must not depend on the server's
            // locale. The same reasoning as the log formatter in Program.cs.
            return string.Join('|', key.Properties.Select(p =>
                Convert.ToString(entry.Property(p.Name).CurrentValue, CultureInfo.InvariantCulture)));
        }
    }
}
