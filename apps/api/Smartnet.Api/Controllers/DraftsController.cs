using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Smartnet.Api.Auditing;
using Smartnet.Api.Auth;
using Smartnet.Api.Contracts;
using Smartnet.Domain.Auditing;
using Smartnet.Domain.Documents;
using Smartnet.Domain.Identity;
using Smartnet.Infrastructure.Persistence;

namespace Smartnet.Api.Controllers;

/// <summary>
/// Autosaved work on the four create screens — quotation, invoice, purchase order and job card.
/// </summary>
/// <remarks>
/// <para><b>What this is for.</b> Until now, everything typed on a create screen lived in the browser and
/// was posted whole (D4). That is still true of the save — but it meant a closed tab, an expired session
/// or a stray reload lost a forty-line invoice, and people rebuild those from paper. A draft is that
/// state, held on the server, so the work survives the browser.</para>
///
/// <para><b>A draft is not a document</b> — no number, no ledger, no stock, and invisible to the legacy
/// app. See <see cref="DocumentDraft"/> for why that has to be a table of its own rather than a status
/// column on <c>quotation_h</c>.</para>
///
/// <para><b>Authorisation is per draft type, in code, not per endpoint.</b> One controller serves four
/// document types whose create endpoints require four different permissions, so a single
/// <c>[RequirePermission]</c> on the class would have to be either the wrong one or a new, looser one —
/// and a draft carries the same commercial detail as the document it will become. Instead every action
/// resolves the permission from the draft's own <c>doc_type</c>
/// (<see cref="DraftDocumentTypes.PermissionByType"/>) and checks it against the caller's claims, which
/// makes the rule exactly "you may hold a draft when you may raise the document". The class carries
/// <c>[Authorize]</c> so the endpoint-authorisation test still sees a decision declared; the per-type
/// check is what actually decides.</para>
///
/// <para><b>Company-scoped.</b> A draft belongs to the company it was raised in and is only ever read
/// back within it — the same boundary the documents themselves keep. Shared inside that boundary: whoever
/// may raise the document may resume a colleague's draft of it.</para>
/// </remarks>
[ApiController]
[Route("api/drafts")]
[Authorize]
public sealed class DraftsController : ControllerBase
{
    private readonly SmartnetDbContext _db;
    private readonly ICompanyContext _company;
    private readonly IChangeContext _change;
    private readonly TimeProvider _time;

    public DraftsController(
        SmartnetDbContext db,
        ICompanyContext company,
        IChangeContext change,
        TimeProvider time)
    {
        _db = db;
        _company = company;
        _change = change;
        _time = time;
    }

    /// <summary>The active company's unraised drafts of one type, most recently touched first.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<DraftSummary>>> List(
        [FromQuery] string docType,
        CancellationToken cancellationToken)
    {
        if (Denied(docType) is { } denial)
        {
            return denial;
        }

        if (_company.Active is not { } companyId)
        {
            return Ok(Array.Empty<DraftSummary>());
        }

        var drafts = await _db.DocumentDrafts
            .Where(d => d.CompanyId == companyId && d.DocType == docType)
            // Most recently touched first: the draft somebody is working on is the one they came for.
            .OrderByDescending(d => d.UpdatedAt)
            .ThenByDescending(d => d.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var names = await NamesOf(drafts, cancellationToken).ConfigureAwait(false);

        return Ok(drafts.Select(d => new DraftSummary(
            d.Id,
            d.DocType,
            d.PartyName,
            d.Total,
            d.LineCount,
            d.CreatedAt,
            Name(names, d.CreatedBy),
            d.UpdatedAt,
            Name(names, d.UpdatedBy),
            d.RowVersion)).ToList());
    }

    /// <summary>One draft in full, with the state to load back into the create screen.</summary>
    [HttpGet("{id:long}")]
    public async Task<ActionResult<DraftDetail>> Get(long id, CancellationToken cancellationToken)
    {
        if (await Visible(id, cancellationToken).ConfigureAwait(false) is not { } draft)
        {
            return NotFound();
        }

        if (Denied(draft.DocType) is { } denial)
        {
            return denial;
        }

        var names = await NamesOf([draft], cancellationToken).ConfigureAwait(false);

        return Ok(new DraftDetail(
            draft.Id,
            draft.DocType,
            draft.Payload,
            draft.PartyName,
            draft.Total,
            draft.LineCount,
            draft.CreatedAt,
            Name(names, draft.CreatedBy),
            draft.UpdatedAt,
            Name(names, draft.UpdatedBy),
            draft.RowVersion));
    }

    /// <summary>Starts a draft — the first autosave a create screen makes.</summary>
    [HttpPost]
    public async Task<ActionResult<DraftSaved>> Create(
        [FromBody] SaveDraftRequest request,
        CancellationToken cancellationToken)
    {
        if (Denied(request.DocType) is { } denial)
        {
            return denial;
        }

        if (Invalid(request) is { } problem)
        {
            return BadRequest(problem);
        }

        if (_company.Active is not { } companyId)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "No company is selected, so there is nowhere to keep this draft.");
        }

        var now = _time.GetUtcNow().UtcDateTime;

        var draft = new DocumentDraft
        {
            DocType = request.DocType,
            CompanyId = companyId,
            Payload = request.Payload,
            PartyName = Trimmed(request.PartyName),
            Total = request.Total,
            LineCount = request.LineCount,
            CreatedBy = _change.UserId,
            CreatedAt = now,
            UpdatedBy = _change.UserId,
            UpdatedAt = now,
            // Stamped here rather than by the audit interceptor, which never sees this entity — a draft
            // is not IAuditable, so that autosave does not write an audit_log diff every few seconds.
            RowVersion = 1,
        };

        _db.DocumentDrafts.Add(draft);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Ok(new DraftSaved(draft.Id, draft.RowVersion, draft.UpdatedAt));
    }

    /// <summary>Every autosave after the first.</summary>
    /// <remarks>
    /// <b>A stale <paramref name="expectedRowVersion"/> is refused, not merged.</b> Drafts are shared, so
    /// two people can have the same one open; last-write-wins would silently discard whichever of them
    /// stopped typing first, which is the legacy behaviour this rebuild exists to remove. The 409 stops
    /// the autosave loop and the screen says who changed it.
    /// </remarks>
    [HttpPut("{id:long}")]
    public async Task<ActionResult<DraftSaved>> Update(
        long id,
        [FromQuery] int expectedRowVersion,
        [FromBody] SaveDraftRequest request,
        CancellationToken cancellationToken)
    {
        if (Invalid(request) is { } problem)
        {
            return BadRequest(problem);
        }

        if (await Visible(id, cancellationToken).ConfigureAwait(false) is not { } draft)
        {
            return NotFound();
        }

        // The stored type governs, not the one in the body: a caller who may draft invoices must not be
        // able to reach a quotation draft by posting the type they hold.
        if (Denied(draft.DocType) is { } denial)
        {
            return denial;
        }

        if (draft.RowVersion != expectedRowVersion)
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "This draft was changed somewhere else. Reload it to see the other version.");
        }

        draft.Payload = request.Payload;
        draft.PartyName = Trimmed(request.PartyName);
        draft.Total = request.Total;
        draft.LineCount = request.LineCount;
        draft.UpdatedBy = _change.UserId;
        draft.UpdatedAt = _time.GetUtcNow().UtcDateTime;
        draft.RowVersion++;

        try
        {
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            // The check above closes the window it can see; this closes the one it cannot. Two autosaves
            // that read the same version and then write are both past that check, and only the row_version
            // condition in the UPDATE separates them. Without this the loser gets a 500, and a screen that
            // has hit a perfectly ordinary conflict tells its user the server broke.
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "This draft was changed somewhere else. Reload it to see the other version.");
        }

        return Ok(new DraftSaved(draft.Id, draft.RowVersion, draft.UpdatedAt));
    }

    /// <summary>Discards a draft, or clears it once its document has been raised.</summary>
    /// <remarks>
    /// <b>A hard delete, and no change reason.</b> Nothing that mattered is lost: either the draft became
    /// a real document — which is audited, versioned and permanent — or somebody decided not to raise it,
    /// and a tombstoned scratchpad answers no question anyone will ask. There is no row_version check
    /// either: discarding is unconditional by intent, and refusing to delete something because a
    /// colleague typed into it first would leave a draft nobody can get rid of.
    /// </remarks>
    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id, CancellationToken cancellationToken)
    {
        if (await Visible(id, cancellationToken).ConfigureAwait(false) is not { } draft)
        {
            // Already gone — most often because the raise that followed this one's own save deleted it.
            // Answering 404 would turn a successful save into a red toast on the screen behind it.
            return NoContent();
        }

        if (Denied(draft.DocType) is { } denial)
        {
            return denial;
        }

        // ExecuteDelete, not Remove + SaveChanges. `row_version` is a concurrency token, so the tracked
        // delete would carry it into the WHERE clause and fail if a colleague had typed into the draft
        // between this request reading it and deleting it — leaving a draft that refuses to be discarded
        // for as long as somebody else keeps autosaving it. Discarding is unconditional by intent, and
        // this is the statement that says so.
        await _db.DocumentDrafts
            .Where(d => d.Id == draft.Id)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        return NoContent();
    }

    /// <summary>
    /// The draft with this id if the caller's company owns it — otherwise null, whether it does not exist
    /// or belongs to another company.
    /// </summary>
    private async Task<DocumentDraft?> Visible(long id, CancellationToken cancellationToken)
    {
        if (_company.Active is not { } companyId)
        {
            return null;
        }

        return await _db.DocumentDrafts
            .FirstOrDefaultAsync(d => d.Id == id && d.CompanyId == companyId, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// The refusal for this draft type, or null when the caller may have it.
    /// </summary>
    /// <remarks>
    /// An unknown type is a 400 rather than a 403: it is not a permission the caller lacks, it is a
    /// document that has no drafts at all, and saying so is more useful than implying they could be
    /// granted something.
    /// </remarks>
    private ObjectResult? Denied(string? docType)
    {
        if (DraftDocumentTypes.PermissionFor(docType) is not { } permission)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: $"'{docType}' is not a document type that keeps drafts.");
        }

        // Dev_Admin holds everything by definition — the same rule the generated policies apply, applied
        // here too so this check cannot diverge from them.
        var held =
            User.HasClaim(SmartnetClaims.Permission, permission)
            || User.HasClaim(SmartnetClaims.Permission, Permissions.SystemDevAdmin);

        return held
            ? null
            : Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "You do not have permission to work on drafts of this document.");
    }

    /// <summary>The field rules, or null when the body is acceptable.</summary>
    private static string? Invalid(SaveDraftRequest request)
    {
        if (string.IsNullOrEmpty(request.Payload))
        {
            return "A draft cannot be empty.";
        }

        if (request.Payload.Length > DocumentDraft.MaxPayloadLength)
        {
            return $"This draft is too large to keep (limit {DocumentDraft.MaxPayloadLength:N0} characters).";
        }

        // The payload is opaque, but it still has to be readable back. Storing something that is not JSON
        // would fail on the screen that resumes the draft — long after the save that caused it, with
        // nothing to point at. Checked here, where it can still be refused.
        try
        {
            using var _ = JsonDocument.Parse(request.Payload);
        }
        catch (JsonException)
        {
            return "This draft could not be stored — its contents are not valid JSON.";
        }

        if (request.LineCount < 0)
        {
            return "A draft cannot have a negative number of lines.";
        }

        return request.PartyName is { Length: > DocumentDraft.MaxPartyNameLength }
            ? $"The name must be {DocumentDraft.MaxPartyNameLength} characters or fewer."
            : null;
    }

    /// <summary>Display names for everyone who touched these drafts, in one query.</summary>
    private async Task<Dictionary<long, string?>> NamesOf(
        IReadOnlyCollection<DocumentDraft> drafts,
        CancellationToken cancellationToken)
    {
        var ids = drafts
            .SelectMany(d => new[] { d.CreatedBy, d.UpdatedBy })
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        if (ids.Count == 0)
        {
            return [];
        }

        return await _db.Users
            .Where(u => ids.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Name ?? u.Username, cancellationToken)
            .ConfigureAwait(false);
    }

    private static string? Name(Dictionary<long, string?> names, long? userId) =>
        userId is { } id && names.TryGetValue(id, out var name) ? name : null;

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
