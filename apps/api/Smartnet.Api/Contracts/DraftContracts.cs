namespace Smartnet.Api.Contracts;

/// <summary>
/// One unraised draft in a list — everything the Drafts view shows, without the payload.
/// </summary>
/// <remarks>
/// The payload is deliberately absent. A list of twenty drafts would otherwise carry twenty serialised
/// create screens, and the list shows none of it; the payload is fetched only when a draft is resumed.
/// </remarks>
/// <param name="UpdatedById">
/// Who last saved it, by id rather than name. The list needs to know whether that was the person
/// reading it — "being edited by someone else" must not appear on a draft you are editing yourself —
/// and comparing display names to answer that would go wrong the first time two people share one.
/// </param>
public sealed record DraftSummary(
    long Id,
    string DocType,
    string? PartyName,
    decimal? Total,
    int LineCount,
    DateTime CreatedAt,
    string? CreatedByName,
    DateTime UpdatedAt,
    long? UpdatedById,
    string? UpdatedByName,
    int RowVersion);

/// <summary>A draft in full — the summary plus the create screen's state, for resuming it.</summary>
public sealed record DraftDetail(
    long Id,
    string DocType,
    string Payload,
    string? PartyName,
    decimal? Total,
    int LineCount,
    DateTime CreatedAt,
    string? CreatedByName,
    DateTime UpdatedAt,
    string? UpdatedByName,
    int RowVersion);

/// <summary>
/// An autosave. The same body creates a draft and updates one — what differs is
/// <see cref="ExpectedRowVersion"/>, which is absent on the first save of a screen and present on
/// every one after it.
/// </summary>
/// <param name="DocType">Which create screen this is a draft of — one of <c>DraftDocumentTypes.All</c>.</param>
/// <param name="Payload">
/// The screen's state, as the browser serialised it. Stored verbatim and never parsed by the server —
/// only checked for being well-formed JSON, so that what comes back out can be read back in.
/// </param>
/// <param name="PartyName">The customer or supplier the draft is addressed to, for the list. Null until picked.</param>
/// <param name="Total">The running total in major units, for the list. Null when nothing priceable is typed.</param>
/// <param name="LineCount">How many lines have been typed, for the list.</param>
public sealed record SaveDraftRequest(
    string DocType,
    string Payload,
    string? PartyName,
    decimal? Total,
    int LineCount);

/// <summary>
/// What an autosave returns: the draft's id and its new version, which the next autosave must echo back.
/// </summary>
/// <remarks>
/// Deliberately not the whole draft. The browser already holds the state it just sent, and returning it
/// would race with what the user has typed in the meantime — the response would arrive and overwrite two
/// seconds of typing. Only the id and the version come back, because only those are the server's to know.
/// </remarks>
public sealed record DraftSaved(long Id, int RowVersion, DateTime UpdatedAt);
