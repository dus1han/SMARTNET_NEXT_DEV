namespace Smartnet.Domain.Documents;

/// <summary>
/// What may be uploaded (Phase 7, slice 4) — the whitelist, the size cap, and the name generator.
/// </summary>
/// <remarks>
/// <para><b>One place, deliberately.</b> The upload endpoint and the legacy-BLOB migration both admit files,
/// and a whitelist that lived in the controller would apply to one and not the other — so a <c>.exe</c>
/// sitting in <c>docstore</c> since 2019 would walk straight onto disk during the migration.</para>
///
/// <para><b>A whitelist, not a blacklist.</b> Anything not named here is refused. The list is what this
/// business actually exchanges: PDFs, office documents, images.</para>
/// </remarks>
public static class DocumentPolicy
{
    /// <summary>The cap on a single upload. Generous for a scan, far below anything that hurts the disk.</summary>
    public const long MaxBytes = 25 * 1024 * 1024;

    /// <summary>
    /// Extension → the content type it is served as.
    /// </summary>
    /// <remarks>
    /// The download is served as the type recorded here rather than the one the browser claimed at upload,
    /// so a file cannot be stored as a PDF and later served as HTML — which is how an attachment becomes a
    /// script running on our own origin.
    /// </remarks>
    private static readonly Dictionary<string, string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".doc"] = "application/msword",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xls"] = "application/vnd.ms-excel",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".csv"] = "text/csv",
        [".txt"] = "text/plain",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
    };

    /// <summary>The permitted extensions, for a message or an <c>accept</c> attribute.</summary>
    public static IReadOnlyCollection<string> AllowedExtensions => Allowed.Keys;

    /// <summary>
    /// The extension of <paramref name="fileName"/> if it is allowed, else null.
    /// </summary>
    /// <remarks>
    /// Takes only the extension and discards everything else about the name — so a file called
    /// <c>../../etc/passwd.pdf</c> yields <c>.pdf</c> and nothing more. The directory separators never
    /// survive this call.
    /// </remarks>
    public static string? ExtensionOf(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return null;

        var extension = Path.GetExtension(fileName.Trim());
        return !string.IsNullOrEmpty(extension) && Allowed.ContainsKey(extension)
            ? extension.ToLowerInvariant()
            : null;
    }

    /// <summary>The content type to store and serve for an allowed extension.</summary>
    public static string ContentTypeFor(string extension) =>
        Allowed.TryGetValue(extension, out var type) ? type : "application/octet-stream";

    /// <summary>
    /// Strips a filename down to something safe to show and to send as a download name.
    /// </summary>
    /// <remarks>
    /// Path components are dropped rather than escaped: only the leaf is ever of interest, and a name that
    /// arrives with separators in it is either a browser quirk or an attempt.
    /// </remarks>
    public static string SafeDisplayName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return "document";

        var leaf = Path.GetFileName(fileName.Trim());
        if (string.IsNullOrWhiteSpace(leaf)) return "document";

        // Control characters and quotes would break the Content-Disposition header they end up in.
        var cleaned = new string(leaf.Where(c => !char.IsControl(c) && c != '"').ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "document" : Truncate(cleaned, 255);
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
