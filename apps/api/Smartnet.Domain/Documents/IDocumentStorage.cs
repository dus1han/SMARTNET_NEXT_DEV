namespace Smartnet.Domain.Documents;

/// <summary>
/// Where uploaded bytes actually live (Phase 7, slice 4).
/// </summary>
/// <remarks>
/// An interface so the local-filesystem backend can become S3/MinIO as a config change rather than a
/// rewrite — the plan's "no object storage yet, but do not paint into a corner".
/// </remarks>
public interface IDocumentStorage
{
    /// <summary>
    /// Writes <paramref name="content"/> and returns the server-generated name to store on the row.
    /// </summary>
    /// <param name="content">The incoming stream. Read once, forward-only — do not assume it is seekable.</param>
    /// <param name="extension">
    /// The validated extension, leading dot included. Comes from the whitelist, never from the client's
    /// filename directly.
    /// </param>
    /// <returns>The generated name, and the SHA-256 of what was written, computed as it was written.</returns>
    Task<StoredFile> SaveAsync(Stream content, string extension, CancellationToken cancellationToken);

    /// <summary>Opens a stored file for reading, or null if the bytes are gone.</summary>
    /// <remarks>
    /// Returns a stream rather than a byte array so a download never materialises the whole file in memory
    /// — and never anywhere under the web root.
    /// </remarks>
    Task<Stream?> OpenReadAsync(string storedName, CancellationToken cancellationToken);

    /// <summary>Removes a stored file. Succeeds silently if it is already gone.</summary>
    Task DeleteAsync(string storedName, CancellationToken cancellationToken);

    /// <summary>Whether the bytes for a row are actually present — used to verify the legacy migration.</summary>
    Task<bool> ExistsAsync(string storedName, CancellationToken cancellationToken);
}

/// <summary>What a save produced: the name to record, and the hash of the bytes written.</summary>
public sealed record StoredFile(string StoredName, string Sha256, long ByteSize);
