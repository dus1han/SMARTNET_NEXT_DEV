using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Smartnet.Domain.Documents;

namespace Smartnet.Infrastructure.Storage;

/// <summary>
/// The local-filesystem document store (Phase 7, slice 4).
/// </summary>
/// <remarks>
/// <para><b>Outside the web root, always.</b> The legacy app wrote uploads under the site directory, so every
/// file was fetchable by anyone who guessed the name and no permission check stood between them and it
/// (Finding C3). Nothing here is reachable by URL: the only way out is the download endpoint, which checks
/// the permission and the company first and then streams the bytes.</para>
///
/// <para><b>Names are generated, never derived from the upload.</b> A GUID plus the validated extension. Two
/// files called <c>scan.pdf</c> cannot collide, and no client-supplied text reaches a path.</para>
///
/// <para>Files are fanned out across 256 subdirectories by the first byte of the name. One flat directory
/// works fine at today's 18 documents and becomes unpleasant to list at a hundred thousand.</para>
/// </remarks>
public sealed partial class LocalFileDocumentStorage : IDocumentStorage
{
    private readonly string _root;
    private readonly ILogger<LocalFileDocumentStorage> _logger;

    public LocalFileDocumentStorage(
        IOptions<DocumentStorageOptions> options,
        ILogger<LocalFileDocumentStorage> logger)
    {
        _logger = logger;
        _root = Path.GetFullPath(options.Value.RootPath);
        Directory.CreateDirectory(_root);
    }

    public async Task<StoredFile> SaveAsync(Stream content, string extension, CancellationToken cancellationToken)
    {
        var storedName = $"{Guid.NewGuid():N}{extension}";
        var path = ResolveForWrite(storedName);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        long size;
        byte[] hash;

        // Hash while writing rather than re-reading the file afterwards: one pass over the bytes, and the
        // hash describes exactly what landed rather than what a second read found.
        using (var sha = SHA256.Create())
        {
            await using (var destination = new FileStream(
                path, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 81920, useAsync: true))
            await using (var hashing = new CryptoStream(destination, sha, CryptoStreamMode.Write))
            {
                await content.CopyToAsync(hashing, cancellationToken).ConfigureAwait(false);
                await hashing.FlushFinalBlockAsync(cancellationToken).ConfigureAwait(false);

                size = destination.Length;
            }

            hash = sha.Hash ?? [];
        }

        return new StoredFile(storedName, Convert.ToHexString(hash).ToLowerInvariant(), size);
    }

    public Task<Stream?> OpenReadAsync(string storedName, CancellationToken cancellationToken)
    {
        if (!TryResolveForRead(storedName, out var path) || !File.Exists(path))
        {
            return Task.FromResult<Stream?>(null);
        }

        Stream stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 81920, useAsync: true);

        return Task.FromResult<Stream?>(stream);
    }

    public Task DeleteAsync(string storedName, CancellationToken cancellationToken)
    {
        if (TryResolveForRead(storedName, out var path) && File.Exists(path))
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException ex)
            {
                // A file we cannot remove is not a reason to fail the request — the metadata row is already
                // soft-deleted, so the document is gone as far as anyone can tell. Logged so it can be swept.
                LogDeleteFailed(_logger, storedName, ex);
            }
        }

        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string storedName, CancellationToken cancellationToken) =>
        Task.FromResult(TryResolveForRead(storedName, out var path) && File.Exists(path));

    private string ResolveForWrite(string storedName) => Path.Combine(_root, storedName[..2], storedName);

    /// <summary>
    /// Maps a stored name to a path, refusing anything that escapes the root.
    /// </summary>
    /// <remarks>
    /// The names we generate could never escape — but this reads names out of the database, and "the database
    /// only ever contains names we generated" is an assumption, not a guarantee. The check costs nothing and
    /// removes the assumption.
    /// </remarks>
    private bool TryResolveForRead(string storedName, out string path)
    {
        path = string.Empty;

        if (string.IsNullOrWhiteSpace(storedName) || storedName.Length < 3) return false;
        if (storedName.Contains('/') || storedName.Contains('\\') || storedName.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        var candidate = Path.GetFullPath(Path.Combine(_root, storedName[..2], storedName));

        if (!candidate.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            LogPathEscaped(_logger, storedName);
            return false;
        }

        path = candidate;
        return true;
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Could not delete stored document {StoredName}")]
    private static partial void LogDeleteFailed(ILogger logger, string storedName, Exception exception);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "Refused a stored-document path outside the root: {StoredName}")]
    private static partial void LogPathEscaped(ILogger logger, string storedName);
}

/// <summary>Where the document store keeps its files.</summary>
public sealed class DocumentStorageOptions
{
    public const string Section = "DocumentStorage";

    /// <summary>
    /// The storage root. Must be outside the web root, and on a path that survives a redeploy — in Docker
    /// that means a mounted volume, or every upload is lost the next time the container is replaced.
    /// </summary>
    public string RootPath { get; set; } = "documents";
}
