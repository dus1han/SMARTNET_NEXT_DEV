using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Smartnet.Domain.Auditing;
using Smartnet.Domain.Documents;
using Smartnet.Domain.Ledger;
using Smartnet.Infrastructure.Persistence;
using Smartnet.Infrastructure.Persistence.Configurations;

namespace Smartnet.Infrastructure.Documents;

/// <summary>
/// Records expenses (Phase 7, slice 3) — a validated, audited write on the adopted legacy <c>expense_tr</c>
/// table. What was incurred; <see cref="ExpensePaymentService"/> is what pays it.
/// </summary>
/// <remarks>
/// The typed columns are the source of truth; the legacy <c>varchar</c> columns are dual-written beside them
/// (via shadow properties) so the surviving <c>ExpenseReport</c> reads a whole row. Void is soft and
/// reason-gated — not the legacy hard delete — and refused while any payment stands against the expense.
/// </remarks>
public sealed class ExpenseService : IExpenseCreator, IExpenseVoider
{
    private readonly SmartnetDbContext _db;
    private readonly IGeneralLedger _gl;
    private readonly IChangeContext _change;
    private readonly TimeProvider _time;

    public ExpenseService(SmartnetDbContext db, IGeneralLedger gl, IChangeContext change, TimeProvider time)
    {
        _db = db;
        _gl = gl;
        _change = change;
        _time = time;
    }

    public async Task<ExpenseCreated> CreateAsync(NewExpense request, CancellationToken cancellationToken = default)
    {
        var company = await _db.Companies
            .FirstOrDefaultAsync(c => c.Id == request.CompanyId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Company {request.CompanyId} does not exist.");

        var category = await _db.ExpenseCategories
            .FirstOrDefaultAsync(c => c.Id == request.CategoryId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Expense category {request.CategoryId} does not exist.");

        var expense = new Expense
        {
            CompanyId = request.CompanyId,
            CategoryId = request.CategoryId,
            Date = request.Date,
            InvoiceNo = request.InvoiceNo,
            Description = request.Description,
            NetAmount = request.NetAmount,
            TaxRatePercentage = request.TaxRatePercentage,
            VatNumber = string.IsNullOrWhiteSpace(request.VatNumber) ? null : request.VatNumber.Trim(),
            Amount = request.Amount,
            // Empty until a payment settles it — the method and reference describe money going out, and none
            // has yet. ExpensePaymentService mirrors the latest settlement onto these legacy columns.
            Method = string.Empty,
            Reference = string.Empty,
            DataOrigin = "new",
        };

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        _db.Expenses.Add(expense);
        SetLegacyShadow(expense, await ActingUserNameAsync(cancellationToken).ConfigureAwait(false));
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // The general-ledger entry: Dr the category's expense account + Input VAT, Cr Accounts Payable — the
        // cost is incurred now and owed; the money leaves when a payment settles it (Dr Payable, Cr Cash/Bank).
        // The expense account is created on demand for a category first seen since the chart was seeded.
        await _gl.PostAsync(new GlPosting(
            request.CompanyId, request.Date, GlSources.Expense, expense.Id, request.Description,
            [
                GlChart.ExpenseCategory(request.CategoryId, category.Name ?? $"Category {request.CategoryId}", expense.NetAmount, 0m),
                GlChart.InputVat(expense.TaxAmount, 0m),
                GlChart.Payable(0m, expense.Amount),
            ]), cancellationToken).ConfigureAwait(false);

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

        // Money that has already gone out is never left pointing at an expense that no longer exists: the
        // payments come off first, each reversing what it posted, and only then can the expense be voided.
        var payments = await _db.ExpensePayments
            .CountAsync(p => p.ExpenseId == expenseId, cancellationToken)
            .ConfigureAwait(false);
        if (payments > 0)
        {
            throw new ExpenseHasPaymentsException(expenseId, payments);
        }

        var now = _time.GetUtcNow().UtcDateTime;

        // What the void reverses is whatever the expense actually posted, which is not the same for every
        // row: an expense recorded since expenses became payable credited Accounts Payable, while one
        // recorded before that (and every adopted legacy row) credited Cash/Bank as it was entered.
        var postedToPayable = await _db.GlLines
            .AnyAsync(
                l => _db.GlEntries.Any(e => e.Id == l.GlEntryId && e.SourceType == GlSources.Expense && e.SourceId == expense.Id)
                     && _db.GlAccounts.Any(a => a.Id == l.AccountId && a.Code == GlAccountCodes.AccountsPayable),
                cancellationToken)
            .ConfigureAwait(false);

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        // Soft delete — the legacy delete hard-removed the row; here its history is kept.
        expense.DeletedAt = now;
        expense.DeletedBy = _change.UserId;

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Reverse the expense's GL entry — Dr what it credited, Cr the category account + Input VAT.
        if (expense.CompanyId is { } companyId)
        {
            await _gl.PostAsync(new GlPosting(
                companyId, DateOnly.FromDateTime(now), GlSources.ExpenseVoid, expense.Id,
                $"Expense {expense.Id} voided",
                [
                    postedToPayable
                        ? GlChart.Payable(expense.Amount, 0m)
                        : GlChart.CashOrBank(expense.Method, expense.Amount, 0m),
                    GlChart.ExpenseCategory(expense.CategoryId, $"Category {expense.CategoryId}", 0m, expense.NetAmount),
                    GlChart.InputVat(0m, expense.TaxAmount),
                ]), cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
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
