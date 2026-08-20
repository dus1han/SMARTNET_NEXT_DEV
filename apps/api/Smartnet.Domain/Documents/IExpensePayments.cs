namespace Smartnet.Domain.Documents;

/// <summary>
/// A settlement to record against an expense — part of what it still owes, or all of it. When
/// <paramref name="Method"/> is <c>Cheque</c>, the cheque fields raise a printable cheque for this payment.
/// </summary>
public sealed record RecordExpensePayment(
    decimal Amount,
    DateOnly Date,
    string? Method,
    string? Reference,
    string? ChequePayee = null,
    string? ChequeBank = null,
    string? ChequeNumber = null,
    DateOnly? ChequeDate = null,
    DateOnly? ChequeDueDate = null);

/// <summary>What a settlement returns — including the derived outstanding after it, so the UI can show "settled" at zero.</summary>
public sealed record ExpensePaymentRecorded(long ExpenseId, long PaymentId, decimal AmountPaid, decimal Outstanding);

/// <summary>Thrown when a settlement would pay more than an expense still owes.</summary>
public sealed class ExpensePaymentExceedsOutstandingException(decimal outstanding, decimal attempted)
    : Exception($"The payment of {attempted:0.00} exceeds the {outstanding:0.00} still outstanding on this expense.")
{
    public decimal Outstanding { get; } = outstanding;
    public decimal Attempted { get; } = attempted;
}

/// <summary>
/// Thrown when an expense that has been settled, in part or in full, is voided. The payments come off first,
/// so money that has already gone out is never left pointing at an expense that no longer exists.
/// </summary>
public sealed class ExpenseHasPaymentsException(long expenseId, int payments)
    : Exception($"This expense has {payments} payment(s) against it and cannot be voided. Void the payments first, then void the expense.")
{
    public long ExpenseId { get; } = expenseId;
    public int Payments { get; } = payments;
}

/// <summary>
/// Settles expenses — the money side of an <see cref="Expense"/>, which records only what was incurred.
/// </summary>
/// <remarks>
/// An expense is entered unpaid and settled afterwards, in one payment or several. Each payment posts
/// Dr Accounts Payable, Cr Cash/Bank inside its own transaction, so what is outstanding stays derived
/// (amount − Σ payments) and partial settlement needs no flag. A void is soft and reason-gated, and reverses
/// only what the payment actually posted — a backfilled <see cref="ExpensePaymentOrigin.Migrated"/> payment
/// posted nothing, because the expense it settles paid Cash/Bank at the moment it was recorded.
/// </remarks>
public interface IExpensePayments
{
    /// <summary>Records a settlement against an expense, and returns what is left outstanding after it.</summary>
    Task<ExpensePaymentRecorded> RecordPaymentAsync(long expenseId, RecordExpensePayment payment, CancellationToken cancellationToken = default);

    /// <summary>Voids a settlement — soft, reason-gated; the expense goes back to being outstanding by that much.</summary>
    Task VoidPaymentAsync(long paymentId, int expectedRowVersion, CancellationToken cancellationToken = default);
}
