namespace Smartnet.Domain.Reporting;

/// <summary>Renders a proforma invoice to a printable PDF, in its company's own profile.</summary>
public interface IProformaRenderer
{
    /// <summary>The proforma for <paramref name="proformaId"/> as a PDF — voided ones too — or null if it does not exist.</summary>
    Task<byte[]?> RenderAsync(long proformaId, CancellationToken cancellationToken = default);
}
