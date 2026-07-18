namespace Smartnet.Domain.Documents;

/// <summary>One allocation of a receipt being created — how much goes against which invoice.</summary>
public sealed record NewReceiptAllocation(long InvoiceId, decimal Amount);

/// <summary>A whole customer receipt, posted at once — the total is the sum of its allocations.</summary>
public sealed record NewCustomerReceipt(
    long CompanyId,
    long CustomerId,
    DateOnly Date,
    string? Method,
    string? Reference,
    string IdempotencyKey,
    IReadOnlyList<NewReceiptAllocation> Allocations);

/// <summary>What the caller gets back. <paramref name="AlreadyExisted"/> is true when the idempotency key matched an existing receipt.</summary>
public sealed record CustomerReceiptCreated(long Id, decimal Amount, bool AlreadyExisted);

/// <summary>Thrown when an allocation would settle more than an invoice still owes.</summary>
public sealed class ReceiptAllocationExceedsOutstandingException(long invoiceId, decimal outstanding, decimal attempted)
    : Exception($"The allocation of {attempted:0.00} exceeds the {outstanding:0.00} outstanding on invoice {invoiceId}.")
{
    public long InvoiceId { get; } = invoiceId;
    public decimal Outstanding { get; } = outstanding;
    public decimal Attempted { get; } = attempted;
}

/// <summary>Thrown when an invoice in the receipt does not belong to the receipt's customer.</summary>
public sealed class ReceiptInvoiceCustomerMismatchException(long invoiceId)
    : Exception($"Invoice {invoiceId} does not belong to this customer and cannot be settled by this receipt.")
{
    public long InvoiceId { get; } = invoiceId;
}

/// <summary>
/// Creates a customer receipt — the whole of it, in one transaction (Phase 7, slice 1).
/// </summary>
/// <remarks>
/// For each allocation: a receivables-ledger <c>Payment</c> entry (negative), a dual-written legacy
/// <c>payments</c> row, and <c>invoice_h.balance -= amount</c>. An overpay (an allocation above the invoice's
/// derived outstanding) is refused; a resubmit with the same <see cref="NewCustomerReceipt.IdempotencyKey"/>
/// returns the existing receipt (Finding 1). It settles new and legacy invoices alike.
/// </remarks>
public interface ICustomerReceiptCreator
{
    Task<CustomerReceiptCreated> CreateAsync(NewCustomerReceipt request, CancellationToken cancellationToken = default);
}

/// <summary>Voids a customer receipt — soft, reason-gated, reversing the ledger and the legacy shadow.</summary>
public interface ICustomerReceiptVoider
{
    Task VoidAsync(long receiptId, int expectedRowVersion, CancellationToken cancellationToken = default);

    /// <summary>
    /// Voids a payment the old system took, identified by its <c>payments</c> row id.
    /// </summary>
    /// <remarks>
    /// A legacy payment has no <c>customer_receipts</c> row to void — it was never a receipt in this
    /// system, only a row in the old table, which is why the lists show it under a negative id. Since the
    /// ledger was rebuilt from the documents it does have a <c>Payment</c> entry, so voiding it is the
    /// same reversal every other void performs: a compensating <c>Charge</c> that puts the receivable
    /// back, a reversing negative row in the legacy table so the old reports net to nothing, and
    /// <c>invoice_h.balance</c> restored.
    ///
    /// <para>Nothing is erased. The original payment row and its ledger entry both survive; what changes
    /// is that a reversal now sits beside them.</para>
    /// </remarks>
    Task VoidLegacyAsync(long legacyPaymentId, CancellationToken cancellationToken = default);
}
