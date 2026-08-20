using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Smartnet.Domain.Auditing;
using Smartnet.Domain.Documents;
using Smartnet.Domain.Ledger;
using Smartnet.Infrastructure.Persistence;
using Smartnet.Infrastructure.Persistence.Configurations;
using Smartnet.Infrastructure.Reporting;

namespace Smartnet.Infrastructure.Documents;

/// <summary>
/// Settles expenses — the money side of an expense, which is recorded when it is incurred and paid
/// afterwards, in full or in instalments.
/// </summary>
/// <remarks>
/// What is outstanding is derived here (the expense's total minus its live payments) rather than stored, so
/// there is no "paid" flag to keep in step and a part payment needs no special case. Each payment posts
/// Dr Accounts Payable, Cr Cash/Bank — but only when the expense itself raised that payable. An expense
/// recorded before expenses could be settled separately, and every adopted legacy one, credited Cash/Bank as
/// it was entered; posting again for its settlement would spend the money twice, so those settle silently.
/// That is also why the backfilled <see cref="ExpensePaymentOrigin.Migrated"/> rows post nothing.
/// </remarks>
public sealed class ExpensePaymentService : IExpensePayments
{
    private readonly SmartnetDbContext _db;
    private readonly IChequeCreator _cheques;
    private readonly IGeneralLedger _gl;
    private readonly IChangeContext _change;
    private readonly TimeProvider _time;

    public ExpensePaymentService(
        SmartnetDbContext db,
        IChequeCreator cheques,
        IGeneralLedger gl,
        IChangeContext change,
        TimeProvider time)
    {
        _db = db;
        _cheques = cheques;
        _gl = gl;
        _change = change;
        _time = time;
    }

    public async Task<ExpensePaymentRecorded> RecordPaymentAsync(long expenseId, RecordExpensePayment payment, CancellationToken cancellationToken = default)
    {
        if (payment.Amount <= 0m)
        {
            throw new InvalidOperationException("A payment must be for more than zero.");
        }

        // IgnoreQueryFilters so an adopted legacy expense can be settled too — it sits in the same table and
        // is money owed just the same, whichever app recorded it.
        var expense = await _db.Expenses
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.Id == expenseId && e.DeletedAt == null, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Expense {expenseId} does not exist.");

        var outstanding = TotalOf(expense) - await PaidAsync(expenseId, cancellationToken).ConfigureAwait(false);
        if (payment.Amount > outstanding)
        {
            throw new ExpensePaymentExceedsOutstandingException(outstanding, payment.Amount);
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var row = new ExpensePayment
        {
            ExpenseId = expenseId,
            // An adopted legacy row may carry its company only in the legacy varchar, and a payment with no
            // company could not be found again by a company-scoped screen — so fall back to that column.
            CompanyId = expense.CompanyId ?? LegacyCompanyOf(expense),
            Date = payment.Date,
            Amount = payment.Amount,
            Method = payment.Method,
            Reference = payment.Reference,
            DataOrigin = ExpensePaymentOrigin.New,
        };
        _db.ExpensePayments.Add(row);

        // The legacy expense_tr row carries a single method/reference and the surviving ExpenseReport reads
        // only that row, so the latest settlement is mirrored onto it — the settlements themselves are here.
        MirrorOntoExpense(expense, payment.Method, payment.Reference);

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false); // assigns row.Id

        // Paid by cheque → raise a printable cheque for this payment (the payment is the money event).
        if (string.Equals(payment.Method, "Cheque", StringComparison.OrdinalIgnoreCase) && row.CompanyId is { } chequeCompany)
        {
            await _cheques.CreateAsync(new NewCheque(
                chequeCompany, "Manual", payment.ChequePayee ?? expense.Description, null,
                payment.ChequeBank, payment.ChequeNumber, payment.Amount,
                payment.ChequeDate ?? payment.Date, payment.ChequeDueDate ?? payment.Date,
                ChequeSource.ExpensePayment, row.Id), cancellationToken).ConfigureAwait(false);
        }

        // Dr Accounts Payable, Cr Cash/Bank — settling what the expense left owed. Skipped when the expense
        // never raised a payable (see the remarks): the money it represents has already left the books once.
        if (row.CompanyId is { } companyId && await RaisedAPayableAsync(expenseId, cancellationToken).ConfigureAwait(false))
        {
            await _gl.PostAsync(new GlPosting(
                companyId, payment.Date, GlSources.ExpensePayment, row.Id, payment.Reference,
                [
                    GlChart.Payable(payment.Amount, 0m),
                    GlChart.CashOrBank(payment.Method, 0m, payment.Amount),
                ]), cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return new ExpensePaymentRecorded(expenseId, row.Id, payment.Amount, outstanding - payment.Amount);
    }

    public async Task VoidPaymentAsync(long paymentId, int expectedRowVersion, CancellationToken cancellationToken = default)
    {
        var payment = await _db.ExpensePayments
            .FirstOrDefaultAsync(p => p.Id == paymentId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Expense payment {paymentId} does not exist.");

        if (payment.RowVersion != expectedRowVersion)
        {
            throw new DbUpdateConcurrencyException(
                "This payment was changed by someone else while you were viewing it.");
        }

        // Whether this payment posted anything decides whether the void reverses anything — a migrated one
        // never did, so voiding it simply puts the expense back to outstanding.
        var posted = await _db.GlEntries
            .AnyAsync(e => e.SourceType == GlSources.ExpensePayment && e.SourceId == paymentId, cancellationToken)
            .ConfigureAwait(false);

        var now = _time.GetUtcNow().UtcDateTime;

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        payment.DeletedAt = now;
        payment.DeletedBy = _change.UserId;

        // The mirrored legacy method/reference follow the settlement that is now the latest surviving one.
        var expense = await _db.Expenses
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.Id == payment.ExpenseId, cancellationToken)
            .ConfigureAwait(false);
        if (expense is not null)
        {
            var latest = await _db.ExpensePayments
                .Where(p => p.ExpenseId == payment.ExpenseId && p.Id != paymentId)
                .OrderByDescending(p => p.Date)
                .ThenByDescending(p => p.Id)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
            MirrorOntoExpense(expense, latest?.Method, latest?.Reference);
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Reverse it — Dr Cash/Bank, Cr Accounts Payable: the money comes back and the expense is owed again.
        if (posted && payment.CompanyId is { } companyId)
        {
            await _gl.PostAsync(new GlPosting(
                companyId, DateOnly.FromDateTime(now), GlSources.ExpensePaymentVoid, payment.Id,
                $"Expense payment {payment.Id} voided",
                [
                    GlChart.CashOrBank(payment.Method, payment.Amount, 0m),
                    GlChart.Payable(0m, payment.Amount),
                ]), cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The company off the legacy <c>company</c> varchar, for a row that has no typed company_id.</summary>
    private long? LegacyCompanyOf(Expense expense) =>
        long.TryParse(
            _db.Entry(expense).Property<string>(ExpenseLegacyShadow.Company).CurrentValue,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var companyId)
            ? companyId
            : null;

    /// <summary>What the expense is for: the typed total on a new row, the legacy varchar on an adopted one.</summary>
    private decimal TotalOf(Expense expense) =>
        expense.DataOrigin == "new"
            ? expense.Amount
            : LegacyValue.Money(_db.Entry(expense).Property<string>(ExpenseLegacyShadow.ExpenseAmount).CurrentValue);

    /// <summary>What has been settled so far — voided payments do not count (the query filter excludes them).</summary>
    private async Task<decimal> PaidAsync(long expenseId, CancellationToken cancellationToken) =>
        await _db.ExpensePayments
            .Where(p => p.ExpenseId == expenseId)
            .SumAsync(p => (decimal?)p.Amount, cancellationToken)
            .ConfigureAwait(false) ?? 0m;

    /// <summary>Whether recording the expense credited Accounts Payable — see the remarks on this class.</summary>
    private Task<bool> RaisedAPayableAsync(long expenseId, CancellationToken cancellationToken) =>
        _db.GlLines.AnyAsync(
            l => _db.GlEntries.Any(e => e.Id == l.GlEntryId && e.SourceType == GlSources.Expense && e.SourceId == expenseId)
                 && _db.GlAccounts.Any(a => a.Id == l.AccountId && a.Code == GlAccountCodes.AccountsPayable),
            cancellationToken);

    private static void MirrorOntoExpense(Expense expense, string? method, string? reference)
    {
        expense.Method = method ?? string.Empty;
        expense.Reference = reference ?? string.Empty;
    }
}
