using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Smartnet.Api.Auditing;
using Smartnet.Api.Auth;
using Smartnet.Api.Contracts;
using Smartnet.Domain.Documents;
using Smartnet.Domain.Identity;
using Smartnet.Infrastructure.Persistence;

namespace Smartnet.Api.Controllers;

/// <summary>
/// Document storage (Phase 7, slice 4) — upload, list, download, remove.
/// </summary>
/// <remarks>
/// <para><b>The bytes never touch the web root.</b> The legacy app wrote uploads under the site directory, so
/// anyone who guessed a filename had the file with no permission check in the way (Finding C3). Here the only
/// route to a file is <see cref="Download"/>, which checks the permission and the company before it opens a
/// stream — and the store itself lives on a path no web server serves.</para>
///
/// <para><b>The database holds metadata only.</b> The legacy <c>docstore</c> kept a <c>LONGBLOB</c> in the row
/// (Finding C4), so listing titles dragged the file contents along.</para>
///
/// <para>Documents may stand alone in the library, or attach to a record via
/// <c>entityType</c>/<c>entityId</c> — the same row either way.</para>
/// </remarks>
[ApiController]
[Route("api/documents")]
public sealed class DocumentsController : ControllerBase
{
    private readonly SmartnetDbContext _db;
    private readonly IDocumentStorage _storage;
    private readonly ICompanyContext _company;

    public DocumentsController(SmartnetDbContext db, IDocumentStorage storage, ICompanyContext company)
    {
        _db = db;
        _storage = storage;
        _company = company;
    }

    /// <summary>Documents the caller may see — the whole library, or just one record's attachments.</summary>
    [HttpGet]
    [RequirePermission(Permissions.DocStorage)]
    public async Task<ActionResult<IReadOnlyList<DocumentSummary>>> List(
        [FromQuery] string? entityType,
        [FromQuery] long? entityId,
        CancellationToken cancellationToken)
    {
        var accessible = _company.Accessible.ToList();

        var query = _db.StoredDocuments.Where(d => accessible.Contains(d.CompanyId));

        // Both or neither: an entityType with no id would silently list every invoice's attachments.
        if (!string.IsNullOrWhiteSpace(entityType) && entityId is { } id)
        {
            query = query.Where(d => d.EntityType == entityType && d.EntityId == id);
        }

        var rows = await query
            .OrderByDescending(d => d.CreatedAt)
            .Select(d => new DocumentSummary(
                d.Id, d.Title, d.OriginalFileName, d.ContentType, d.ByteSize,
                d.EntityType, d.EntityId, d.CreatedAt, d.RowVersion))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Ok(rows);
    }

    /// <summary>Uploads a file against the active company.</summary>
    /// <remarks>
    /// Validated server-side and only server-side. The web form applies the same whitelist to give an
    /// immediate answer, but that is a convenience — this is the authority.
    /// </remarks>
    [HttpPost]
    [RequirePermission(Permissions.DocStorage)]
    [RequestSizeLimit(DocumentPolicy.MaxBytes + 8192)]
    public async Task<ActionResult<DocumentSummary>> Upload(
        IFormFile file,
        [FromForm] string? title,
        [FromForm] string? entityType,
        [FromForm] long? entityId,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest("No file was uploaded.");
        }

        if (file.Length > DocumentPolicy.MaxBytes)
        {
            return BadRequest($"The file must be {DocumentPolicy.MaxBytes / (1024 * 1024)} MB or smaller.");
        }

        // Required, not defaulted to the filename. The title is what the document is listed and searched
        // under, and "scan0001.pdf" is a document nobody finds again.
        if (string.IsNullOrWhiteSpace(title))
        {
            return BadRequest("A title is required.");
        }

        // The extension decides, not the browser's content type — which is client-supplied and, for the
        // office formats especially, frequently wrong or absent.
        if (DocumentPolicy.ExtensionOf(file.FileName) is not { } extension)
        {
            return BadRequest(
                "That file type is not accepted. Allowed: " +
                string.Join(", ", DocumentPolicy.AllowedExtensions.Order(StringComparer.Ordinal)) + ".");
        }

        if (_company.Active is not { } companyId)
        {
            return NotFound();
        }

        await using var incoming = file.OpenReadStream();
        var stored = await _storage.SaveAsync(incoming, extension, cancellationToken).ConfigureAwait(false);

        var displayName = DocumentPolicy.SafeDisplayName(file.FileName);

        var document = new StoredDocument
        {
            CompanyId = companyId,
            Title = title.Trim(),
            OriginalFileName = displayName,
            StoredName = stored.StoredName,
            ContentType = DocumentPolicy.ContentTypeFor(extension),
            ByteSize = stored.ByteSize,
            Sha256 = stored.Sha256,
            EntityType = string.IsNullOrWhiteSpace(entityType) ? null : entityType.Trim(),
            EntityId = string.IsNullOrWhiteSpace(entityType) ? null : entityId,
        };

        _db.StoredDocuments.Add(document);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Ok(new DocumentSummary(
            document.Id, document.Title, document.OriginalFileName, document.ContentType, document.ByteSize,
            document.EntityType, document.EntityId, document.CreatedAt, document.RowVersion));
    }

    /// <summary>Streams a document's bytes back.</summary>
    /// <remarks>
    /// Streamed rather than buffered, so a 25 MB file does not become 25 MB of server memory per concurrent
    /// download. Served as the content type recorded at upload — never one the client asks for.
    /// </remarks>
    /// <param name="inline">
    /// Render in place rather than download — what the preview dialog asks for.
    /// </param>
    [HttpGet("{id:long}/content")]
    [RequirePermission(Permissions.DocStorage)]
    public async Task<IActionResult> Download(
        long id,
        [FromQuery] bool inline,
        CancellationToken cancellationToken)
    {
        var accessible = _company.Accessible.ToList();

        var document = await _db.StoredDocuments
            .Where(d => d.Id == id && accessible.Contains(d.CompanyId))
            .Select(d => new { d.StoredName, d.ContentType, d.OriginalFileName })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (document is null)
        {
            return NotFound();
        }

        var stream = await _storage.OpenReadAsync(document.StoredName, cancellationToken).ConfigureAwait(false);

        if (stream is null)
        {
            // The row survived its file. Worth distinguishing from "no such document": this one is a
            // storage fault to investigate, not a bad id.
            return Problem(
                statusCode: StatusCodes.Status410Gone,
                title: "The file for this document is missing from storage.");
        }

        // Inline is safe *because of* the whitelist, not in spite of it. Rendering attacker-supplied
        // content on our own origin is how an upload becomes stored XSS — but HTML, SVG and scripts are
        // not admissible types, so what can arrive here is a PDF, an image or an office file, none of
        // which the browser executes as script against this origin.
        //
        // Passing a filename is what makes it an attachment: File(stream, type, name) sets
        // Content-Disposition: attachment, and the browser downloads instead of rendering.
        return inline
            ? File(stream, document.ContentType)
            : File(stream, document.ContentType, document.OriginalFileName);
    }

    /// <summary>Removes a document — soft on the row, and the bytes with it.</summary>
    /// <remarks>
    /// The row is soft-deleted so the audit trail keeps who uploaded what and when, but the file itself is
    /// removed from disk. Keeping the bytes of a document somebody deleted would be the surprising choice.
    /// </remarks>
    [HttpDelete("{id:long}")]
    [RequirePermission(Permissions.DocStorage)]
    [RequireChangeReason]
    public async Task<IActionResult> Delete(
        long id,
        [FromQuery] int expectedRowVersion,
        CancellationToken cancellationToken)
    {
        var accessible = _company.Accessible.ToList();

        var document = await _db.StoredDocuments
            .FirstOrDefaultAsync(d => d.Id == id && accessible.Contains(d.CompanyId), cancellationToken)
            .ConfigureAwait(false);

        if (document is null)
        {
            return NotFound();
        }

        if (document.RowVersion != expectedRowVersion)
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "This document was changed by someone else. Reload and try again.");
        }

        var storedName = document.StoredName;

        // Soft-delete first. If removing the file then fails, the document is already unreachable — the
        // other order would leave a row pointing at bytes that are gone.
        document.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await _storage.DeleteAsync(storedName, cancellationToken).ConfigureAwait(false);

        return NoContent();
    }
}
