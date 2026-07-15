namespace Smartnet.Domain.Auditing;

/// <summary>
/// One row of the append-only audit log. Written by the SaveChanges interceptor inside the same
/// transaction as the business change it describes — so audit and data cannot diverge: either
/// both commit or neither does.
/// </summary>
/// <remarks>
/// The application's database user is granted INSERT and SELECT on this table, never UPDATE or
/// DELETE (see infra/sql/audit-log-grants.sql). An append-only log that the app can rewrite is
/// not evidence. That is enforced by a GRANT, not by this comment.
/// <para>
/// There are deliberately no audit columns on this type: the audit log is not itself auditable,
/// it is immutable.
/// </para>
/// </remarks>
public class AuditLogEntry
{
    public long Id { get; set; }

    public long? CompanyId { get; set; }

    /// <summary>The CLR entity name — "Invoice", "Customer", "UserPermission".</summary>
    public string EntityType { get; set; } = null!;

    /// <summary>
    /// Stringified primary key. A string because keys are not uniformly typed across the legacy
    /// schema, and because a composite key has to serialise to something.
    /// </summary>
    public string EntityId { get; set; } = null!;

    public AuditAction Action { get; set; }

    /// <summary>Null only for anonymous events — a failed login has no user.</summary>
    public long? ChangedBy { get; set; }

    /// <summary>UTC.</summary>
    public DateTime ChangedAt { get; set; }

    /// <summary>The "why". See AUDIT.md §5 for when it is mandatory.</summary>
    public string? Reason { get; set; }

    /// <summary>
    /// <c>{ "field": { "from": x, "to": y }, ... }</c> — only the fields that actually changed,
    /// not the whole row. Redacted fields appear here with their values masked.
    /// </summary>
    public string? Changes { get; set; }

    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? CorrelationId { get; set; }
}
