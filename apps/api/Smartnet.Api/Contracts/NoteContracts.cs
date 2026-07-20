namespace Smartnet.Api.Contracts;

// --- Personal notes (Phase 7, slice 5) ----------------------------------------------------------
//
// A note has a title and a body and belongs to the person who wrote it. No author field goes over the
// wire: every note in a response is the caller's own, so naming them would be telling them who they are.

/// <summary>A note, as the list shows it.</summary>
/// <remarks>
/// The body travels with the summary rather than behind a second request. Notes are small, a person has
/// few of them, and the edit dialog needs the body the moment it opens — fetching it separately would
/// buy nothing and cost a round trip on every edit.
/// </remarks>
public sealed record NoteSummary(
    long Id,
    string Title,
    string Body,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    int RowVersion);

/// <summary>Creates a note.</summary>
public sealed record CreateNoteRequest(
    string Title,
    string Body);

/// <summary>Rewrites a note's title and body.</summary>
public sealed record UpdateNoteRequest(
    string Title,
    string Body,
    int ExpectedRowVersion);
