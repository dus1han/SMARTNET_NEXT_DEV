namespace Smartnet.Domain.Settings;

/// <summary>
/// A company's logo image, stored in the database (there is no blob store to stand up for two companies).
/// One row per company; it prints on that company's documents beside the name. Kept in its own table, not a
/// column on <see cref="Company"/>, so the (potentially large) bytes never load on an ordinary company read.
/// </summary>
public sealed class CompanyLogo
{
    public long CompanyId { get; set; }

    /// <summary>The image MIME type (e.g. <c>image/png</c>) — served back verbatim so the browser and the
    /// PDF renderer decode it correctly.</summary>
    public string ContentType { get; set; } = null!;

    public byte[] Data { get; set; } = null!;

    public DateTime UpdatedAt { get; set; }

    public long? UpdatedBy { get; set; }
}
