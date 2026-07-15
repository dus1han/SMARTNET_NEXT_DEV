using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Smartnet.Domain.Auditing;
using Smartnet.Infrastructure.Persistence;

namespace Smartnet.Infrastructure.Auditing;

/// <inheritdoc cref="IDocumentVersionWriter"/>
public sealed class DocumentVersionWriter : IDocumentVersionWriter
{
    private readonly SmartnetDbContext _db;
    private readonly IChangeContext _change;
    private readonly TimeProvider _time;

    public DocumentVersionWriter(SmartnetDbContext db, IChangeContext change, TimeProvider time)
    {
        _db = db;
        _change = change;
        _time = time;
    }

    public async Task<int> WriteAsync(
        string docType,
        long docId,
        long? companyId,
        object snapshot,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        // The next version is one past the highest this document already has. Max over an empty set is
        // null, which is version 0 — so a document's first snapshot is version 1.
        var last = await _db.DocumentVersions
            .Where(v => v.DocType == docType && v.DocId == docId)
            .MaxAsync(v => (int?)v.VersionNo, cancellationToken)
            .ConfigureAwait(false);

        var versionNo = (last ?? 0) + 1;

        _db.DocumentVersions.Add(new DocumentVersion
        {
            CompanyId = companyId,
            DocType = docType,
            DocId = docId,
            VersionNo = versionNo,
            Snapshot = JsonSerializer.Serialize(snapshot),
            ChangedBy = _change.UserId,
            ChangedAt = _time.GetUtcNow().UtcDateTime,
            Reason = reason,
        });

        // Saved within the caller's transaction (the caller opened it). document_versions is not
        // itself auditable, so this does not recurse through the audit interceptor.
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return versionNo;
    }
}
