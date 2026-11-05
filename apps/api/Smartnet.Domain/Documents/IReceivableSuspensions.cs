namespace Smartnet.Domain.Documents;

/// <summary>
/// The suspended account: an invoice's outstanding moved off it, and later brought back — on its own, or
/// with the payment that settles it.
/// </summary>
/// <remarks>
/// Each move is a receivables-ledger entry (so every outstanding figure follows), the legacy
/// <c>invoice_h.balance</c> shadow set to the new derived outstanding (so the dashboard, reports, dunning
/// and the old app follow too), and a GL posting between Accounts Receivable and Suspended Receivables.
/// What the customer owes — and so the credit limit — does not change.
/// </remarks>
public interface IReceivableSuspensions
{
    Task<SuspensionRecorded> SuspendAsync(SuspendReceivable request, CancellationToken cancellationToken = default);

    /// <summary>Puts the whole suspended amount back on the invoice.</summary>
    Task ReinstateAsync(long suspensionId, int expectedRowVersion, string? note, CancellationToken cancellationToken = default);

    /// <summary>
    /// Puts the suspended amount back and records the receipt against the invoice, in one transaction. A
    /// payment for less than the amount leaves the rest on the invoice's ordinary outstanding.
    /// </summary>
    Task<CustomerReceiptCreated> SettleAsync(long suspensionId, int expectedRowVersion, SettleSuspension payment, CancellationToken cancellationToken = default);
}

public sealed record SuspendReceivable(long InvoiceId, decimal Amount, DateOnly Date, string Reason);

public sealed record SuspensionRecorded(long Id, decimal Amount);

public sealed record SettleSuspension(decimal Amount, DateOnly Date, string? Method, string? Reference, string IdempotencyKey);

/// <summary>Thrown when an amount to suspend is more than the invoice still owes.</summary>
public sealed class SuspensionExceedsOutstandingException(string invoiceNumber, decimal outstanding, decimal attempted)
    : Exception($"Invoice {invoiceNumber} has {outstanding:0.00} outstanding — {attempted:0.00} cannot be suspended.");

/// <summary>Thrown when an invoice already has an amount on the suspended account.</summary>
public sealed class InvoiceAlreadySuspendedException(string invoiceNumber)
    : Exception($"Invoice {invoiceNumber} already has an amount on the suspended account. Reinstate it first.");

/// <summary>Thrown when an invoice with a suspended amount would be edited or voided.</summary>
public sealed class InvoiceIsSuspendedException(string invoiceNumber)
    : Exception($"Invoice {invoiceNumber} has an amount on the suspended account. Reinstate it first, then change the invoice.");

/// <summary>Thrown when a suspension that is no longer on the account is reinstated or settled.</summary>
public sealed class SuspensionNotActiveException(long id)
    : Exception($"Suspension {id} has already been reinstated.");
