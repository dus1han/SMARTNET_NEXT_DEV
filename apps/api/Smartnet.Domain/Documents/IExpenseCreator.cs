namespace Smartnet.Domain.Documents;

/// <summary>A new expense to record.</summary>
public sealed record NewExpense(
    long CompanyId,
    long CategoryId,
    DateOnly Date,
    string Description,
    decimal Amount,
    string? Method,
    string? Reference);

/// <summary>What the caller gets back after recording an expense.</summary>
public sealed record ExpenseCreated(long Id, decimal Amount);

/// <summary>
/// Records an expense — a validated, audited write that dual-writes the full legacy <c>expense_tr</c> row so
/// the surviving <c>ExpenseReport</c> keeps reading (Phase 7, slice 3). Standalone: no ledger, no balance.
/// </summary>
public interface IExpenseCreator
{
    Task<ExpenseCreated> CreateAsync(NewExpense request, CancellationToken cancellationToken = default);
}

/// <summary>Voids an expense — soft, reason-gated (not the legacy hard delete).</summary>
public interface IExpenseVoider
{
    Task VoidAsync(long expenseId, int expectedRowVersion, CancellationToken cancellationToken = default);
}
