namespace Smartnet.Domain.Reporting;

/// <summary>Renders a credit note to a printable PDF, in its company's own profile.</summary>
public interface ICreditNoteRenderer
{
    /// <summary>The note for <paramref name="creditNoteId"/> as a PDF, or null if it does not exist.</summary>
    Task<byte[]?> RenderAsync(long creditNoteId, CancellationToken cancellationToken = default);
}
