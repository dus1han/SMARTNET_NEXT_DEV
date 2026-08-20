namespace Smartnet.Domain.Documents;

/// <summary>
/// A new expense to record — what was incurred, unpaid. How it is paid is not part of recording it: the
/// money goes out through <see cref="RecordExpensePayment"/>, once or in instalments.
/// </summary>
public sealed record NewExpense(
    long CompanyId,
    long CategoryId,
    DateOnly Date,
    string? InvoiceNo,
    string Description,
    decimal NetAmount,
    decimal TaxRatePercentage,
    string? VatNumber,
    decimal Amount);

/// <summary>What the caller gets back after recording an expense.</summary>
public sealed record ExpenseCreated(long Id, decimal Amount);

/// <summary>
/// Records an expense — a validated, audited write that dual-writes the full legacy <c>expense_tr</c> row so
/// the surviving <c>ExpenseReport</c> keeps reading (Phase 7, slice 3). The expense is recorded as owed
/// (Dr the category + Input VAT, Cr Accounts Payable); <see cref="IExpensePayments"/> settles it.
/// </summary>
public interface IExpenseCreator
{
    Task<ExpenseCreated> CreateAsync(NewExpense request, CancellationToken cancellationToken = default);
}

/// <summary>Voids an expense — soft, reason-gated (not the legacy hard delete), and refused while it has payments.</summary>
public interface IExpenseVoider
{
    Task VoidAsync(long expenseId, int expectedRowVersion, CancellationToken cancellationToken = default);
}
