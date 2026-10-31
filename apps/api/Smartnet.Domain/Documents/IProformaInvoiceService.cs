namespace Smartnet.Domain.Documents;

/// <summary>
/// Quotation → proforma → invoice.
/// </summary>
/// <remarks>
/// The rules, each enforced here rather than left to the screens:
/// <list type="bullet">
/// <item>A quotation has at most one live (not voided) proforma.</item>
/// <item>While it has one, the quotation cannot be converted straight to an invoice, edited or voided —
/// it goes through the proforma, so one quotation can never become two invoices.</item>
/// <item>A proforma converts to an invoice once. That invoice is raised through the ordinary invoice
/// pipeline, and the quotation is marked converted to it as well.</item>
/// <item>Voiding a proforma frees its quotation. A converted proforma cannot be voided — void the
/// invoice instead.</item>
/// </list>
/// </remarks>
public interface IProformaInvoiceService
{
    /// <summary>Raises a proforma from a quotation, valued at <paramref name="proformaDate"/>.</summary>
    Task<ProformaCreated> CreateFromQuotationAsync(long quotationId, DateOnly proformaDate, CancellationToken cancellationToken = default);

    /// <summary>Raises the invoice. The terms are the same a quotation conversion asks for.</summary>
    Task<InvoiceCreated> ConvertToInvoiceAsync(long proformaId, ConvertQuotation request, CancellationToken cancellationToken = default);

    /// <summary>Voids the proforma — soft, and only while it has not been converted.</summary>
    Task<ProformaVoided> VoidAsync(long proformaId, int expectedRowVersion, CancellationToken cancellationToken = default);
}

public sealed record ProformaCreated(long Id, string Number, decimal Total);

public sealed record ProformaVoided(long Id, string Number);

/// <summary>The quotation already has a live proforma, so it must go through that one.</summary>
public sealed class QuotationHasProformaException(string quotationNumber, string proformaNumber)
    : Exception($"Quotation {quotationNumber} has proforma invoice {proformaNumber}. Convert or void the proforma instead.")
{
    public string ProformaNumber { get; } = proformaNumber;
}

/// <summary>The proforma has already been converted to an invoice.</summary>
public sealed class ProformaAlreadyConvertedException(string proformaNumber)
    : Exception($"Proforma invoice {proformaNumber} has already been converted to an invoice.");
