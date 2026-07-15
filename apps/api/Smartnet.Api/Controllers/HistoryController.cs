using Microsoft.AspNetCore.Mvc;
using Smartnet.Api.Auth;
using Smartnet.Api.Contracts;
using Smartnet.Domain.Auditing;
using Smartnet.Domain.Identity;
using Smartnet.Domain.Settings;

namespace Smartnet.Api.Controllers;

/// <summary>
/// The read side of the audit trail — what the History tab is made of.
/// </summary>
/// <remarks>
/// Phase 1 wrote <c>audit_log</c> and <c>document_versions</c> on every save and then gave nobody a
/// way to look at either. This is that way, and it is the only one: every history surface in the
/// application reads through these endpoints, so the company scoping is applied once rather than
/// re-derived on forty screens.
///
/// <para>Guarded by <c>audit.view</c>, which <c>Dev_Admin</c> and <c>Company_Admin</c> both hold and
/// an ordinary business role does not. The history of a record names who did what and from which IP
/// address; it is not list data.</para>
/// </remarks>
[ApiController]
[Route("api/history")]
[RequirePermission(Permissions.AuditView)]
public sealed class HistoryController : ControllerBase
{
    private readonly IAuditHistory _history;
    private readonly ICompanyContext _company;

    public HistoryController(IAuditHistory history, ICompanyContext company)
    {
        _history = history;
        _company = company;
    }

    /// <summary>Everything the audit log holds about one record, newest first.</summary>
    /// <remarks>
    /// <paramref name="entityType"/> is the CLR entity name the interceptor wrote — "User",
    /// "Invoice", "TaxRate" — and <paramref name="entityId"/> its stringified key.
    /// </remarks>
    [HttpGet("records/{entityType}/{entityId}")]
    public async Task<ActionResult<RecordHistoryResponse>> Record(
        string entityType,
        string entityId,
        [FromQuery] int limit = HistoryLimits.Default,
        CancellationToken cancellationToken = default)
    {
        if (!_history.IsAuditableEntity(entityType))
        {
            // Not an empty list. An unknown entity type returns nothing whether it is a typo or a
            // record that has genuinely never been touched, and on a screen whose entire job is to
            // be believed those two must not look the same.
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: $"'{entityType}' is not a type this system keeps history for.");
        }

        var history = await _history
            .ForRecordAsync(entityType, entityId, Scope, limit, cancellationToken)
            .ConfigureAwait(false);

        return Ok(new RecordHistoryResponse(
            [.. history.Events.Select(ToContract)],
            history.Total));
    }

    /// <summary>The versions of one document, newest first, without their snapshots.</summary>
    [HttpGet("documents/{docType}/{docId:long}/versions")]
    public async Task<ActionResult<IReadOnlyList<DocumentVersionSummary>>> Versions(
        string docType,
        long docId,
        CancellationToken cancellationToken)
    {
        if (!DocumentTypes.IsKnown(docType))
        {
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: $"'{docType}' is not a document type.");
        }

        var versions = await _history
            .VersionsAsync(docType, docId, Scope, cancellationToken)
            .ConfigureAwait(false);

        return Ok(versions.Select(v => new DocumentVersionSummary(
            v.Id, v.DocType, v.DocId, v.VersionNo,
            v.ChangedBy, v.ChangedByName, v.ChangedAt, v.Reason)).ToList());
    }

    /// <summary>One version, snapshot included — the diff and the reprint both read this.</summary>
    [HttpGet("documents/{docType}/{docId:long}/versions/{versionNo:int}")]
    public async Task<ActionResult<DocumentVersionDetail>> Version(
        string docType,
        long docId,
        int versionNo,
        CancellationToken cancellationToken)
    {
        if (!DocumentTypes.IsKnown(docType))
        {
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: $"'{docType}' is not a document type.");
        }

        var version = await _history
            .VersionAsync(docType, docId, versionNo, Scope, cancellationToken)
            .ConfigureAwait(false);

        // Null covers both "no such version" and "a version in a company you cannot see". They are
        // deliberately indistinguishable: a 403 on the second would confirm the document exists.
        return version?.Snapshot is null
            ? NotFound()
            : Ok(new DocumentVersionDetail(
                version.Id, version.DocType, version.DocId, version.VersionNo,
                version.ChangedBy, version.ChangedByName, version.ChangedAt, version.Reason,
                version.Snapshot));
    }

    /// <summary>
    /// The companies this caller may read history for — the set baked into their token at sign-in,
    /// not the one company they are currently switched to.
    /// </summary>
    /// <remarks>
    /// The active company is a UI preference. Scoping history to it would mean an administrator who
    /// switched company could no longer see the history of a record they had just been looking at,
    /// which is not a security property — it is a bug that looks like one.
    /// </remarks>
    private HistoryScope Scope => new(_company.Accessible);

    private static AuditEntry ToContract(HistoryEvent e) => new(
        e.Id,
        e.EntityType,
        e.EntityId,
        e.Action.ToString(),
        e.ChangedBy,
        e.ChangedByName,
        e.ChangedAt,
        e.Reason,
        e.Changes,
        e.IpAddress,
        e.CorrelationId);
}
