namespace Smartnet.Api.Contracts;

/// <summary>
/// One entry in the history of a record.
/// </summary>
/// <param name="Changes">
/// The field-level diff: <c>{ "Name": { "from": "…", "to": "…" } }</c>, only the fields that
/// actually changed. Redacted fields appear here with their values masked — the log records
/// <i>that</i> a password changed, never what to.
/// </param>
/// <param name="ChangedByName">
/// Resolved for display. The log itself stores the user <b>id</b>, never a name: the legacy app
/// stored <c>addedby</c> as the string "Saboor A. : 2026-07-14 10:33:12", so renaming a user made
/// its own history ambiguous. The name is looked up at read time and may be null if the user has
/// since been removed — which is exactly why the id is what was stored.
/// </param>
public sealed record AuditEntry(
    long Id,
    string EntityType,
    string EntityId,
    string Action,
    long? ChangedBy,
    string? ChangedByName,
    DateTime ChangedAt,
    string? Reason,
    string? Changes,
    string? IpAddress,
    string? CorrelationId);

/// <summary>
/// The history of one record: what the log holds, and how much of it there is.
/// </summary>
/// <param name="Total">
/// Every event, not just the ones returned — so the screen can say "showing 50 of 312". A list that
/// silently stops at its limit reads as a complete history, which on this screen is a lie.
/// </param>
public sealed record RecordHistoryResponse(IReadOnlyList<AuditEntry> Entries, int Total);

/// <summary>
/// One version of a document, without its snapshot.
/// </summary>
/// <remarks>
/// The snapshot is the entire document. Sending nine of them to draw a list of nine dates is the
/// difference between a tab that opens and one that thinks about it — so the list is metadata, and
/// the snapshot is fetched for the version the user actually selects.
/// </remarks>
public sealed record DocumentVersionSummary(
    long Id,
    string DocType,
    long DocId,
    int VersionNo,
    long? ChangedBy,
    string? ChangedByName,
    DateTime ChangedAt,
    string? Reason);

/// <param name="Snapshot">
/// The complete header, lines and resolved tax as they stood at save, as JSON. Self-contained on
/// purpose: reprinting version 2 of last year's invoice must reproduce <i>that</i> document, not
/// today's VAT rate applied to last year's lines.
/// </param>
public sealed record DocumentVersionDetail(
    long Id,
    string DocType,
    long DocId,
    int VersionNo,
    long? ChangedBy,
    string? ChangedByName,
    DateTime ChangedAt,
    string? Reason,
    string Snapshot);
