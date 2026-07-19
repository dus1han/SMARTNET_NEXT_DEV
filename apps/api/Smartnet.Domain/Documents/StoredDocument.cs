using Smartnet.Domain.Auditing;

namespace Smartnet.Domain.Documents;

/// <summary>
/// An uploaded file (Phase 7, slice 4) — the metadata row. The bytes live on disk, never here.
/// </summary>
/// <remarks>
/// <para><b>Named <c>StoredDocument</c>, not <c>Document</c>, on purpose.</b> In this namespace a "document"
/// already means an invoice, quotation or credit note. This is the other thing — an attachment, a scan, a
/// signed form — and sharing the word would make every future reader disambiguate by context.</para>
///
/// <para><b>The bytes are not in the database.</b> The legacy <c>docstore</c> put a <c>LONGBLOB</c> in a row
/// (Finding C4), so reading a list of titles dragged the file contents with it. Here the row carries only
/// metadata and <see cref="StoredName"/> points at a file under the storage root.</para>
///
/// <para><b><see cref="StoredName"/> is server-generated and <see cref="OriginalFileName"/> is not trusted.</b>
/// A client-supplied name reaches the filesystem in exactly one place — nowhere. The original is kept
/// because it is what the user recognises and what a download should be called, but it never forms a path.</para>
/// </remarks>
public class StoredDocument : IAuditable, ISoftDeletable
{
    public long Id { get; set; }

    /// <summary>The company the document belongs to. Scoped like everything else.</summary>
    public long CompanyId { get; set; }

    /// <summary>What a person calls it. Defaults to the uploaded file's name, and is editable.</summary>
    public string Title { get; set; } = null!;

    /// <summary>The name the file arrived with — shown, and used for the download filename. Never a path.</summary>
    public string OriginalFileName { get; set; } = null!;

    /// <summary>
    /// The server-generated name on disk, including extension. Opaque, unguessable, and the only value
    /// ever joined onto the storage root.
    /// </summary>
    public string StoredName { get; set; } = null!;

    /// <summary>The validated content type, from the whitelist.</summary>
    public string ContentType { get; set; } = null!;

    /// <summary>Size in bytes, recorded at upload so a listing never has to touch the disk.</summary>
    public long ByteSize { get; set; }

    /// <summary>
    /// SHA-256 of the contents, hex. Lets a re-upload be recognised, and lets the legacy migration verify
    /// that what landed on disk is what came out of the BLOB.
    /// </summary>
    public string Sha256 { get; set; } = null!;

    /// <summary>
    /// What this is attached to — <c>invoice</c>, <c>customer</c>, <c>job_card</c> — or null for a document
    /// that stands on its own in the library.
    /// </summary>
    public string? EntityType { get; set; }

    /// <summary>The id of the attached record, paired with <see cref="EntityType"/>.</summary>
    public long? EntityId { get; set; }

    /// <summary>
    /// The <c>docstore.id</c> this row was materialised from, or null for a genuine upload.
    /// </summary>
    /// <remarks>
    /// This is what makes the legacy migration idempotent: the tool skips any <c>docstore</c> row whose id
    /// already appears here, so running it twice does not produce two copies of the same file.
    /// </remarks>
    public int? LegacyDocstoreId { get; set; }

    public long? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public long? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public long? DeletedBy { get; set; }
    public DateTime? DeletedAt { get; set; }
    public int RowVersion { get; set; }
}
