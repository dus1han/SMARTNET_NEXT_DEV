namespace Smartnet.Domain.Documents;

/// <summary>One allocation of a supplier payment — how much goes against which supplier invoice.</summary>
public sealed record NewSupplierPaymentAllocation(long SupplierInvoiceId, decimal Amount);

/// <summary>
/// A whole supplier payment, posted at once — the total is the sum of its allocations. When
/// <paramref name="Method"/> is <c>Cheque</c>, the cheque fields are used to raise a printable cheque linked
/// to this payment (so the cheque is not a second money event).
/// </summary>
public sealed record NewSupplierPayment(
    long CompanyId,
    long SupplierId,
    DateOnly Date,
    string? Method,
    string? Reference,
    string IdempotencyKey,
    IReadOnlyList<NewSupplierPaymentAllocation> Allocations,
    string? ChequeBank = null,
    string? ChequeNumber = null,
    DateOnly? ChequeDate = null,
    DateOnly? ChequeDueDate = null);

/// <summary>What the caller gets back. <paramref name="AlreadyExisted"/> is true when the idempotency key matched an existing payment.</summary>
public sealed record SupplierPaymentCreated(long Id, decimal Amount, bool AlreadyExisted);

/// <summary>Thrown when an allocation would settle more than a supplier invoice still owes.</summary>
public sealed class SupplierPaymentAllocationExceedsOutstandingException(long supplierInvoiceId, decimal outstanding, decimal attempted)
    : Exception($"The allocation of {attempted:0.00} exceeds the {outstanding:0.00} outstanding on supplier invoice {supplierInvoiceId}.")
{
    public long SupplierInvoiceId { get; } = supplierInvoiceId;
    public decimal Outstanding { get; } = outstanding;
    public decimal Attempted { get; } = attempted;
}

/// <summary>Thrown when a supplier invoice in the payment does not belong to the payment's supplier.</summary>
public sealed class SupplierPaymentInvoiceMismatchException(long supplierInvoiceId)
    : Exception($"Supplier invoice {supplierInvoiceId} does not belong to this supplier and cannot be settled by this payment.")
{
    public long SupplierInvoiceId { get; } = supplierInvoiceId;
}

/// <summary>
/// Creates a supplier payment — the whole of it, in one transaction (Phase 7).
/// </summary>
/// <remarks>
/// For each allocation: a payables-ledger <c>Payment</c> entry (negative), a dual-written legacy
/// <c>supplier_inv_pay</c> row, and — once the invoice's derived outstanding reaches zero —
/// <c>paymentstat='Paid'</c>. An overpay is refused; a resubmit with the same
/// <see cref="NewSupplierPayment.IdempotencyKey"/> returns the existing payment. Settles new and legacy alike.
/// </remarks>
public interface ISupplierPaymentCreator
{
    Task<SupplierPaymentCreated> CreateAsync(NewSupplierPayment request, CancellationToken cancellationToken = default);
}

/// <summary>Voids a supplier payment — soft, reason-gated, reversing the ledger and the legacy shadow.</summary>
public interface ISupplierPaymentVoider
{
    Task VoidAsync(long paymentId, int expectedRowVersion, CancellationToken cancellationToken = default);
}
