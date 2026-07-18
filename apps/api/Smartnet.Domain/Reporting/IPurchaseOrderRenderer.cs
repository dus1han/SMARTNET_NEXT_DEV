namespace Smartnet.Domain.Reporting;

/// <summary>Renders a purchase order to a printable PDF, in its company's own profile.</summary>
public interface IPurchaseOrderRenderer
{
    /// <summary>The order for <paramref name="orderId"/> as a PDF, or null if it does not exist.</summary>
    Task<byte[]?> RenderAsync(long orderId, CancellationToken cancellationToken = default);
}
