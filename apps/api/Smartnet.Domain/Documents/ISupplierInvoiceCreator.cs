namespace Smartnet.Domain.Documents;

/// <summary>
/// A whole supplier invoice, posted at once — header-only (no lines): the supplier's own reference, the
/// figures they billed, and the company we owe from.
/// </summary>
public sealed record NewSupplierInvoice(
    long CompanyId,
    long SupplierId,
    string? SupplierReference,
    DateOnly Date,
    decimal NetTotal,
    decimal TaxRatePercentage,
    decimal Amount);

/// <summary>What the caller gets back — enough to show a toast and route to the new supplier invoice.</summary>
public sealed record SupplierInvoiceCreated(long Id, string? SupplierReference, decimal Amount);

/// <summary>
/// Records a supplier invoice — the whole of it, in one transaction (Phase 6, slice 2).
/// </summary>
/// <remarks>
/// No number is allocated (the supplier's own reference identifies it; the surrogate id is the key) and no
/// stock moves (goods enter via a PO or a stock adjustment). The header is written with its legacy shadow
/// columns beside it (including <c>paymentstat = 'Pending'</c>), a <c>Purchase</c> payable entry is posted
/// for the amount, and a version-1 snapshot is taken — <b>all or none</b>. The payable is derived from the
/// ledger from that point on, so partial payments simply accumulate.
/// </remarks>
public interface ISupplierInvoiceCreator
{
    Task<SupplierInvoiceCreated> CreateAsync(NewSupplierInvoice request, CancellationToken cancellationToken = default);
}
