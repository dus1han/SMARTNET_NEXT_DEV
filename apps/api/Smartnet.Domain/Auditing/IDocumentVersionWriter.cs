namespace Smartnet.Domain.Auditing;

/// <summary>
/// Writes a point-in-time snapshot of a document to <c>document_versions</c> — the writer Phase 1
/// built the table, the read side and the History tab for, and then left for Phase 5 to supply.
/// </summary>
/// <remarks>
/// Version 1 is written at <b>creation</b>, so the original is never the one version you cannot recover;
/// each later edit writes the next. The snapshot is whatever the caller hands in — for an invoice, its
/// header, lines and <b>resolved</b> tax and company header, so a reprint reproduces the document as it
/// was issued rather than re-resolving today's rate onto old lines.
/// <para>
/// <b>Called inside the document's save transaction</b>, so the snapshot and the document commit
/// together or not at all — a version that describes a document that failed to save would be a lie the
/// History tab tells.
/// </para>
/// </remarks>
public interface IDocumentVersionWriter
{
    /// <summary>
    /// Appends the next version of a document and returns its number (1 for a new document).
    /// </summary>
    /// <param name="snapshot">Serialised to JSON as-is. Make it self-contained — resolved, not referenced.</param>
    Task<int> WriteAsync(
        string docType,
        long docId,
        long? companyId,
        object snapshot,
        string? reason,
        CancellationToken cancellationToken = default);
}
