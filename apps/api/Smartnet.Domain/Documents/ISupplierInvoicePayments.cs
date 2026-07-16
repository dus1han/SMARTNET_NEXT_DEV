namespace Smartnet.Domain.Documents;

/// <summary>A payment made against a supplier invoice — part or all of what is outstanding.</summary>
public sealed record RecordSupplierPayment(
    decimal Amount,
    DateOnly Date,
    string? Method,
    string? Reference);

/// <summary>What a payment returns — the derived outstanding after it, so the UI can show "settled" at zero.</summary>
public sealed record SupplierPaymentRecorded(long SupplierInvoiceId, decimal AmountPaid, decimal Outstanding);

/// <summary>Thrown when a payment would settle more than a supplier invoice still owes.</summary>
public sealed class SupplierPaymentExceedsOutstandingException(decimal outstanding, decimal attempted)
    : Exception($"The payment of {attempted:0.00} exceeds the {outstanding:0.00} still outstanding on this supplier invoice.")
{
    public decimal Outstanding { get; } = outstanding;
    public decimal Attempted { get; } = attempted;
}

/// <summary>
/// Records payments against a supplier invoice, and voids one (Phase 6, slice 2).
/// </summary>
/// <remarks>
/// A payment posts a <see cref="Smartnet.Domain.Ledger.PayablesLedgerEntryType.Payment"/> entry (negative)
/// in one transaction, dual-writing a legacy <c>supplier_inv_pay</c> row and flipping the legacy
/// <c>paymentstat</c> to <c>Paid</c> once the derived outstanding reaches zero — so the legacy supplier
/// reports keep reading, while the new app owns "paid" as a derived fact. A void is soft and reason-gated:
/// it reverses the invoice's payable to zero through a compensating entry, never by erasing history (the
/// legacy <c>deleteSupInv</c> hard-deleted the row and orphaned its payments — not ported).
/// </remarks>
public interface ISupplierInvoicePayments
{
    Task<SupplierPaymentRecorded> RecordPaymentAsync(long supplierInvoiceId, RecordSupplierPayment payment, CancellationToken cancellationToken = default);

    Task DeleteAsync(long supplierInvoiceId, int expectedRowVersion, CancellationToken cancellationToken = default);
}
