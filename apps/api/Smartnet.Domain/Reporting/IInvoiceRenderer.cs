namespace Smartnet.Domain.Reporting;

/// <summary>Renders a non-VAT invoice to a printable PDF, in its company's own profile.</summary>
/// <remarks>
/// The <c>Invoice_ST</c> replacement. A VAT-registered company's invoice returns null: a tax invoice
/// carries its own legal content and is a separate document, so this one refuses rather than printing a
/// VAT-registered company's invoice with no VAT on it.
/// </remarks>
public interface IInvoiceRenderer
{
    /// <summary>
    /// The invoice for <paramref name="invoiceId"/> as a PDF. Null when it does not exist, or when its
    /// company is VAT-registered and therefore needs the tax invoice instead.
    /// </summary>
    Task<byte[]?> RenderAsync(long invoiceId, CancellationToken cancellationToken = default);
}
