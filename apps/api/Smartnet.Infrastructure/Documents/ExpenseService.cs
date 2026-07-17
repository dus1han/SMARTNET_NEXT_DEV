using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Smartnet.Domain.Auditing;
using Smartnet.Domain.Documents;
using Smartnet.Infrastructure.Persistence;
using Smartnet.Infrastructure.Persistence.Configurations;

namespace Smartnet.Infrastructure.Documents;

/// <summary>
/// Records expenses (Phase 7, slice 3) — a validated, audited write on the adopted legacy <c>expense_tr</c>
/// table. A flat log: no ledger, no balance.
/// </summary>
/// <remarks>
/// The typed columns are the source of truth; the legacy <c>varchar</c> columns are dual-written beside them
/// (via shadow properties) so the surviving <c>ExpenseReport</c> reads a whole row. Void is soft and
/// reason-gated — not the legacy hard delete.
/// </remarks>
public sealed class ExpenseService : IExpenseCreator, IExpenseVoider
{
    private readonly SmartnetDbContext _db;
    private readonly IChequeCreator _cheques;
    private readonly IChangeContext _change;
    private readonly TimeProvider _time;

    public ExpenseService(SmartnetDbContext db, IChequeCreator cheques, IChangeContext change, TimeProvider time)
    {
        _db = db;
        _cheques = cheques;
        _change = change;
        _time = time;
    }

    public async Task<ExpenseCreated> CreateAsync(NewExpense request, CancellationToken cancellationToken = default)
    {
        var company = await _db.Companies
            .FirstOrDefaultAsync(c => c.Id == request.CompanyId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Company {request.CompanyId} does not exist.");

        _ = await _db.ExpenseCategories
            .FirstOrDefaultAsync(c => c.Id == request.CategoryId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Expense category {request.CategoryId} does not exist.");

        var expense = new Expense
        {
            CompanyId = request.CompanyId,
            CategoryId = request.CategoryId,
            Date = request.Date,
            Description = request.Description,
            NetAmount = request.NetAmount,
            TaxRatePercentage = request.TaxRatePercentage,
            Amount = request.Amount,
            Method = request.Method ?? string.Empty,
            Reference = request.Reference ?? string.Empty,
            DataOrigin = "new",
        };

        var paidByCheque = string.Equals(request.Method, "Cheque", StringComparison.OrdinalIgnoreCase);

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        _db.Expenses.Add(expense);
        SetLegacyShadow(expense, await ActingUserNameAsync(cancellationToken).ConfigureAwait(false));
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Paid by cheque → raise a printable cheque linked to this expense (the expense is the money event).
        if (paidByCheque)
        {
            await _cheques.CreateAsync(new NewCheque(
                request.CompanyId, "Manual", request.ChequePayee ?? request.Description, null,
                request.ChequeBank, request.ChequeNumber, request.Amount,
                request.ChequeDate ?? request.Date, request.ChequeDueDate ?? request.Date,
                ChequeSource.Expense, expense.Id), cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return new ExpenseCreated(expense.Id, expense.Amount);
    }

    public async Task VoidAsync(long expenseId, int expectedRowVersion, CancellationToken cancellationToken = default)
    {
        // IgnoreQueryFilters so a legacy expense (data_origin='legacy') can be voided too — the legacy app let
        // you delete one, and an expense is a flat log with nothing downstream. Soft delete, not the legacy hard one.
        var expense = await _db.Expenses
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.Id == expenseId && e.DeletedAt == null, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Expense {expenseId} does not exist.");

        if (expense.RowVersion != expectedRowVersion)
        {
            throw new DbUpdateConcurrencyException(
                "This expense was changed by someone else while you were viewing it.");
        }

        // Soft delete — the legacy delete hard-removed the row; here its history is kept.
        expense.DeletedAt = _time.GetUtcNow().UtcDateTime;
        expense.DeletedBy = _change.UserId;

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Writes the legacy varchar columns beside the typed ones so the surviving ExpenseReport reads a whole row.</summary>
    private void SetLegacyShadow(Expense expense, string enteredBy)
    {
        var entry = _db.Entry(expense);
        void Set(string name, string value) => entry.Property(name).CurrentValue = value;

        Set(ExpenseLegacyShadow.ExpCat, expense.CategoryId.ToString(CultureInfo.InvariantCulture));
        Set(ExpenseLegacyShadow.ExpenseDate, expense.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Set(ExpenseLegacyShadow.ExpenseAmount, expense.Amount.ToString(CultureInfo.InvariantCulture));
        Set(ExpenseLegacyShadow.AddedBy, enteredBy);
        Set(ExpenseLegacyShadow.AddedDt, _time.GetUtcNow().UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        Set(ExpenseLegacyShadow.Company, expense.CompanyId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
    }

    private async Task<string> ActingUserNameAsync(CancellationToken cancellationToken)
    {
        if (_change.UserId is not { } userId)
        {
            return "system";
        }

        var name = await _db.Users
            .Where(u => u.Id == userId)
            .Select(u => u.Name ?? u.Username)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return name ?? "system";
    }
}
