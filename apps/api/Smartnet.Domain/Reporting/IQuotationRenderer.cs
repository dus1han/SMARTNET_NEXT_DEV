namespace Smartnet.Domain.Reporting;

/// <summary>Renders a quotation to a printable PDF, in its company's own profile.</summary>
public interface IQuotationRenderer
{
    /// <summary>The quotation for <paramref name="quotationId"/> as a PDF, or null if it does not exist.</summary>
    Task<byte[]?> RenderAsync(long quotationId, CancellationToken cancellationToken = default);
}
