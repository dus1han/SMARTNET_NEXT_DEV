namespace Smartnet.Api.Contracts;

// --- Document storage (Phase 7, slice 4) --------------------------------------------------------
//
// Uploaded files: metadata over the wire, bytes only through the download endpoint. Kept apart from
// DocumentContracts.cs, which is about invoices — in this codebase "document" means both things, and
// the two should not share a file.

/// <summary>An uploaded file, as the library and the attachment panel list it.</summary>
/// <remarks>
/// No stored name and no hash: neither is any of the browser's business, and the stored name is the one
/// value that would let a client reason about the layout of the disk.
/// </remarks>
public sealed record DocumentSummary(
    long Id,
    string Title,
    string OriginalFileName,
    string ContentType,
    long ByteSize,
    string? EntityType,
    long? EntityId,
    DateTime UploadedAt,
    int RowVersion);
