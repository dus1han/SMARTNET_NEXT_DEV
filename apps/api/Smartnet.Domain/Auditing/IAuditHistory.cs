namespace Smartnet.Domain.Auditing;

/// <summary>
/// Reads the history of a record back out.
/// </summary>
/// <remarks>
/// Phase 1 built <c>audit_log</c> and <c>document_versions</c> and then never read either of them.
/// A trail nobody can see is a trail nobody trusts — and the first person who asks "who changed
/// this?" is not going to be handed a SQL client. This is the read side, and it is the only one:
/// every history surface in the application goes through it, so the company scoping below cannot
/// be forgotten on one screen out of forty.
/// </remarks>
public interface IAuditHistory
{
    /// <summary>
    /// True when <paramref name="entityType"/> is something the audit log can actually contain.
    /// </summary>
    /// <remarks>
    /// The entity type arrives from the URL. Without this check, <c>/api/history/records/Xyzzy/1</c>
    /// returns an empty list — which reads as "this record has never been changed" when the truth is
    /// "you asked about a thing that does not exist". Those must not look the same on a screen whose
    /// entire job is to be believed.
    /// </remarks>
    bool IsAuditableEntity(string entityType);

    /// <summary>Everything the log holds about one record, newest first.</summary>
    Task<RecordHistory> ForRecordAsync(
        string entityType,
        string entityId,
        HistoryScope scope,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The log across every record, filtered and newest first — the admin audit viewer.
    /// </summary>
    /// <remarks>
    /// The per-record read above answers "what happened to <i>this</i>?"; this one answers the
    /// questions that span records — "who exported the customer list?", "every failed login this
    /// week", "what did this user change?". Same company scoping, same defensive limit: a filter that
    /// matches ten thousand rows returns a page of them and a <see cref="RecordHistory.Total"/> that
    /// tells the truth about the rest, rather than streaming the whole log to a browser.
    /// </remarks>
    Task<RecordHistory> BrowseAsync(
        AuditLogFilter filter,
        HistoryScope scope,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The distinct values the audit viewer offers as filters — the entity types and the users that
    /// actually appear in the log the caller may see.
    /// </summary>
    /// <remarks>
    /// Drawn from the log itself, not from the model or the user table: a dropdown of every
    /// conceivable entity type is a dropdown of mostly dead options, and one of every user lists
    /// people who have never touched anything. The filter offers what is genuinely there — including
    /// values the model does not know about, like the <c>Report</c> an export writes.
    /// </remarks>
    Task<AuditLogFacets> FacetsAsync(
        HistoryScope scope,
        CancellationToken cancellationToken = default);

    /// <summary>The versions of one document, newest first. Snapshots are not loaded.</summary>
    Task<IReadOnlyList<DocumentVersionInfo>> VersionsAsync(
        string docType,
        long docId,
        HistoryScope scope,
        CancellationToken cancellationToken = default);

    /// <summary>One version, snapshot included — for the diff and for reprinting.</summary>
    Task<DocumentVersionInfo?> VersionAsync(
        string docType,
        long docId,
        int versionNo,
        HistoryScope scope,
        CancellationToken cancellationToken = default);
}

/// <summary>How much history one request may ask for.</summary>
/// <remarks>
/// A record with ten thousand audit rows is one whose history is read a page at a time — not one
/// that hangs the browser out of completeness. The limit is clamped server-side, so a client asking
/// for a million rows gets <see cref="Maximum"/> of them rather than a timeout.
/// </remarks>
public static class HistoryLimits
{
    public const int Default = 50;
    public const int Maximum = 500;

    public static int Clamp(int limit) => Math.Clamp(limit, 1, Maximum);
}

/// <summary>
/// The companies whose history the caller may read: the set baked into their token at sign-in.
/// </summary>
/// <remarks>
/// A <c>Dev_Admin</c> needs no special case here — their accessible set <i>is</i> every company
/// (see <c>CompanyAccessService</c>), so one predicate serves both kinds of administrator. A
/// bypass flag would be a second code path, and the one that gets tested is never the one that
/// leaks.
/// </remarks>
public sealed record HistoryScope(IReadOnlySet<long> Companies)
{
    /// <summary>
    /// Rows belonging to no company are visible to anyone who may read history at all.
    /// </summary>
    /// <remarks>
    /// They are not a hole in the scoping: a null <c>company_id</c> means the event happened
    /// outside any set of books. The clearest case is a login — the audit row is written before the
    /// user is authenticated, so at that moment there is no active company to attribute it to.
    /// Excluding those rows would empty a user's history of exactly the events it exists to show.
    /// </remarks>
    public static HistoryScope Of(IEnumerable<long> companies) => new(companies.ToHashSet());
}

/// <param name="Total">
/// How many events exist, not how many were returned. The screen needs to say "showing 50 of 312",
/// because a list that silently stops at its limit reads as a complete history.
/// </param>
public sealed record RecordHistory(IReadOnlyList<HistoryEvent> Events, int Total);

/// <summary>
/// The filters the audit viewer narrows the log by. Every one is optional; an absent filter does not
/// narrow. All of them ride the request — the audit log has no session state to go stale.
/// </summary>
/// <param name="From">Inclusive lower bound on <see cref="AuditLogEntry.ChangedAt"/>, UTC.</param>
/// <param name="To">
/// Exclusive upper bound, UTC. The caller picks a calendar day; the controller turns "to the 14th"
/// into "before the 15th", so an event at 14th 23:59 is included rather than silently dropped.
/// </param>
/// <param name="UserId">The actor. A failed login's actor is the account that was targeted.</param>
/// <param name="Action">One <see cref="AuditAction"/>, or every action when null.</param>
/// <param name="EntityType">The CLR entity name as stored — "User", "Customer", "Report".</param>
/// <param name="Limit">Clamped to <see cref="HistoryLimits"/> server-side.</param>
public sealed record AuditLogFilter(
    DateTime? From,
    DateTime? To,
    long? UserId,
    AuditAction? Action,
    string? EntityType,
    int Limit);

/// <summary>The filterable values actually present in the log the caller may see.</summary>
public sealed record AuditLogFacets(
    IReadOnlyList<string> EntityTypes,
    IReadOnlyList<AuditActor> Actors);

/// <summary>A user who appears in the log, resolved to a name for the filter dropdown.</summary>
public sealed record AuditActor(long Id, string Name);

/// <param name="ChangedByName">
/// Resolved at read time, and null when the user has since been removed. The log stores the user
/// <b>id</b> — the legacy app stored <c>addedby</c> as the string "Saboor A. : 2026-07-14 10:33:12",
/// so renaming a user made its own history ambiguous.
/// </param>
/// <param name="Changes">
/// The field-level diff as stored: <c>{ "Name": { "from": …, "to": … } }</c>. Redacted fields appear
/// with their values masked — the log records <i>that</i> a password changed, never what to.
/// </param>
public sealed record HistoryEvent(
    long Id,
    string EntityType,
    string EntityId,
    AuditAction Action,
    long? ChangedBy,
    string? ChangedByName,
    DateTime ChangedAt,
    string? Reason,
    string? Changes,
    string? IpAddress,
    string? CorrelationId);

/// <param name="Snapshot">
/// Null in a list — a version list that loads every snapshot pulls the whole document history over
/// the wire to render a column of dates. Present when one version is fetched.
/// </param>
public sealed record DocumentVersionInfo(
    long Id,
    string DocType,
    long DocId,
    int VersionNo,
    long? ChangedBy,
    string? ChangedByName,
    DateTime ChangedAt,
    string? Reason,
    string? Snapshot);
