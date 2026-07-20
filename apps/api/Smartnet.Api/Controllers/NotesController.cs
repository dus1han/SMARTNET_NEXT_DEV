using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Smartnet.Api.Auditing;
using Smartnet.Api.Auth;
using Smartnet.Api.Contracts;
using Smartnet.Domain.Auditing;
using Smartnet.Domain.Identity;
using Smartnet.Domain.Notes;
using Smartnet.Infrastructure.Persistence;

namespace Smartnet.Api.Controllers;

/// <summary>
/// Personal notes (Phase 7, slice 5) — list, create, edit, remove.
/// </summary>
/// <remarks>
/// <para><b>Two independent checks guard every action.</b> The <c>notes</c> permission decides who may
/// use the feature at all; <c>created_by</c> decides whose notes they see. Holding the permission does
/// not grant sight of anyone else's notes, so the ownership filter is applied on every read and every
/// write — never assumed from the fact that a caller produced an id.</para>
///
/// <para><b>A missing note and someone else's note are both 404.</b> Answering 403 for a note that
/// exists but belongs to another user would confirm its existence, which is a small leak but a free one
/// to avoid: the caller has no legitimate way to tell the two apart, so neither does the response.</para>
///
/// <para><b>This replaces the legacy shared textarea.</b> The old screen loaded the single newest row of
/// <c>notes</c> and inserted a whole new row on every save — no titles, no list, no edit, no history,
/// and visible to everyone. The legacy table is untouched (LEGACY-DATA-POLICY); nothing here writes it.</para>
/// </remarks>
[ApiController]
[Route("api/notes")]
public sealed class NotesController : ControllerBase
{
    private readonly SmartnetDbContext _db;
    private readonly ICompanyContext _company;
    private readonly IChangeContext _change;

    public NotesController(SmartnetDbContext db, ICompanyContext company, IChangeContext change)
    {
        _db = db;
        _company = company;
        _change = change;
    }

    /// <summary>The caller's own notes, newest first.</summary>
    [HttpGet]
    [RequirePermission(Permissions.Notes)]
    public async Task<ActionResult<IReadOnlyList<NoteSummary>>> List(CancellationToken cancellationToken)
    {
        if (_change.UserId is not { } userId)
        {
            return Unauthorized();
        }

        var rows = await _db.UserNotes
            .Where(n => n.CreatedBy == userId)
            // Most recently touched first — an edited note is the one being worked on.
            .OrderByDescending(n => n.UpdatedAt ?? n.CreatedAt)
            .ThenByDescending(n => n.Id)
            .Select(n => new NoteSummary(n.Id, n.Title, n.Body, n.CreatedAt, n.UpdatedAt, n.RowVersion))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Ok(rows);
    }

    /// <summary>One of the caller's notes.</summary>
    [HttpGet("{id:long}")]
    [RequirePermission(Permissions.Notes)]
    public async Task<ActionResult<NoteSummary>> Get(long id, CancellationToken cancellationToken)
    {
        if (await Owned(id, cancellationToken).ConfigureAwait(false) is not { } note)
        {
            return NotFound();
        }

        return Ok(Summarise(note));
    }

    /// <summary>Creates a note.</summary>
    [HttpPost]
    [RequirePermission(Permissions.Notes)]
    public async Task<ActionResult<NoteSummary>> Create(
        [FromBody] CreateNoteRequest request,
        CancellationToken cancellationToken)
    {
        if (Validate(request.Title, request.Body) is { } problem)
        {
            return BadRequest(problem);
        }

        if (_company.Active is not { } companyId)
        {
            return NotFound();
        }

        var note = new UserNote
        {
            CompanyId = companyId,
            Title = request.Title.Trim(),
            Body = request.Body.Trim(),
        };

        // CreatedBy is set by the audit interceptor from the same IChangeContext the ownership
        // filter reads, so the note cannot be created owned by anyone but its author.
        _db.UserNotes.Add(note);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Ok(Summarise(note));
    }

    /// <summary>Rewrites one of the caller's notes.</summary>
    /// <remarks>
    /// No change reason: the audit log already holds the old and new title and body, so the question a
    /// reason would answer is recorded without asking anyone to type it.
    /// </remarks>
    [HttpPut("{id:long}")]
    [RequirePermission(Permissions.Notes)]
    public async Task<ActionResult<NoteSummary>> Update(
        long id,
        [FromBody] UpdateNoteRequest request,
        CancellationToken cancellationToken)
    {
        if (Validate(request.Title, request.Body) is { } problem)
        {
            return BadRequest(problem);
        }

        if (await Owned(id, cancellationToken).ConfigureAwait(false) is not { } note)
        {
            return NotFound();
        }

        if (note.RowVersion != request.ExpectedRowVersion)
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "This note was changed somewhere else. Reload and try again.");
        }

        note.Title = request.Title.Trim();
        note.Body = request.Body.Trim();
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Ok(Summarise(note));
    }

    /// <summary>Removes one of the caller's notes — soft, so the audit trail survives it.</summary>
    [HttpDelete("{id:long}")]
    [RequirePermission(Permissions.Notes)]
    [RequireChangeReason]
    public async Task<IActionResult> Delete(
        long id,
        [FromQuery] int expectedRowVersion,
        CancellationToken cancellationToken)
    {
        if (await Owned(id, cancellationToken).ConfigureAwait(false) is not { } note)
        {
            return NotFound();
        }

        if (note.RowVersion != expectedRowVersion)
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "This note was changed somewhere else. Reload and try again.");
        }

        note.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return NoContent();
    }

    /// <summary>
    /// The note with this id <i>if it belongs to the caller</i> — otherwise null, whether it does not
    /// exist or simply is not theirs.
    /// </summary>
    private async Task<UserNote?> Owned(long id, CancellationToken cancellationToken)
    {
        if (_change.UserId is not { } userId)
        {
            return null;
        }

        return await _db.UserNotes
            .FirstOrDefaultAsync(n => n.Id == id && n.CreatedBy == userId, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>The shared field rules, or null when the input is acceptable.</summary>
    private static string? Validate(string? title, string? body) =>
        string.IsNullOrWhiteSpace(title) ? "A title is required."
        : title.Trim().Length > UserNote.MaxTitleLength
            ? $"The title must be {UserNote.MaxTitleLength} characters or fewer."
        : string.IsNullOrWhiteSpace(body) ? "A note cannot be empty."
        : body.Trim().Length > UserNote.MaxBodyLength
            ? $"The note must be {UserNote.MaxBodyLength} characters or fewer."
        : null;

    private static NoteSummary Summarise(UserNote note) =>
        new(note.Id, note.Title, note.Body, note.CreatedAt, note.UpdatedAt, note.RowVersion);
}
