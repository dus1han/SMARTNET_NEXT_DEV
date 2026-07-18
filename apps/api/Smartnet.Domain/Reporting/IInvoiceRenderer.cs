namespace Smartnet.Domain.Reporting;

/// <summary>Renders an invoice to a printable PDF, in its company's own profile.</summary>
/// <remarks>
/// Which of the two invoice documents gets rendered follows from what the invoice charged: one carrying
/// VAT, from a registered company, is a tax invoice — naming both parties' registration numbers and the
/// date of supply, because that is what its customer reclaims against — and everything else is a plain
/// invoice. The caller does not choose. The legacy pack left it to whichever report a clerk picked, which
/// is how the same company could issue either.
/// </remarks>
public interface IInvoiceRenderer
{
    /// <summary>The invoice for <paramref name="invoiceId"/> as a PDF, or null if it does not exist.</summary>
    Task<byte[]?> RenderAsync(long invoiceId, CancellationToken cancellationToken = default);
}
