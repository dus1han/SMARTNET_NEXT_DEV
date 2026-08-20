using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Smartnet.Domain.Documents;
using Smartnet.Domain.Ledger;
using Smartnet.Domain.MasterData;
using Smartnet.Domain.Settings;
using Smartnet.Infrastructure.Documents;
using Smartnet.Infrastructure.Ledger;
using Smartnet.Infrastructure.Persistence;
using Smartnet.Tests.Auditing;

namespace Smartnet.Tests.Documents;

/// <summary>
/// Expenses &amp; categories (Phase 7, slice 3): an adopted log of what was incurred. An expense persists its
/// typed columns and dual-writes the legacy <c>expense_tr</c> varchars so the surviving <c>ExpenseReport</c>
/// reads a whole row. It is recorded unpaid and settled afterwards — in full or in instalments — so what it
/// owes is derived, never a flag. Void is soft, not the legacy hard delete, and refused while it has payments.
/// </summary>
[Collection(nameof(AuditCollection))]
public sealed class ExpenseTests
{
    private readonly AuditFixture _fixture;
    private static readonly TimeProvider Clock = TimeProvider.System;

    public ExpenseTests(AuditFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Recording_an_expense_persists_typed_columns_and_dual_writes_the_legacy_shadow()
    {
        var (companyId, categoryId) = await SeedCompanyAndCategory();
        var change = new FakeChangeContext { UserId = 1, CompanyId = companyId };

        ExpenseCreated created;
        await using (var db = _fixture.CreateContext(change))
        {
            created = await Expenses(db, change).CreateAsync(new NewExpense(
                companyId, categoryId, new DateOnly(2026, 7, 17), null, "Petrol", 5000m, 0m, null, 5000m));
        }

        created.Amount.Should().Be(5000m);

        await using (var db = _fixture.CreateContext(change))
        {
            var expense = await db.Expenses.FirstAsync(e => e.Id == created.Id);
            expense.Amount.Should().Be(5000m);
            expense.Description.Should().Be("Petrol");
            expense.CategoryId.Should().Be(categoryId);
            expense.Date.Should().Be(new DateOnly(2026, 7, 17));
            expense.DataOrigin.Should().Be("new");

            // Nothing has been paid yet, so there is no method or reference to show.
            expense.Method.Should().BeEmpty();
            expense.Reference.Should().BeEmpty();

            // The legacy shadow was dual-written for the ExpenseReport.
            var shadow = await db.Database
                .SqlQuery<ExpenseShadow>($"SELECT exp_cat AS ExpCat, expense_amount AS ExpenseAmount, company AS Company FROM expense_tr WHERE id = {created.Id}")
                .SingleAsync();
            shadow.ExpCat.Should().Be(categoryId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            shadow.ExpenseAmount.Should().Be("5000");
            shadow.Company.Should().Be(companyId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    [Fact]
    public async Task An_expense_in_an_unknown_category_is_refused()
    {
        var (companyId, _) = await SeedCompanyAndCategory();
        var change = new FakeChangeContext { UserId = 1, CompanyId = companyId };

        await using var db = _fixture.CreateContext(change);
        var act = () => Expenses(db, change).CreateAsync(new NewExpense(
            companyId, CategoryId: 999999, new DateOnly(2026, 7, 17), null, "Bad", 10m, 0m, null, 10m));

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Voiding_an_expense_soft_deletes_it()
    {
        var (companyId, categoryId) = await SeedCompanyAndCategory();
        var change = new FakeChangeContext { UserId = 1, CompanyId = companyId };

        long id;
        int rowVersion;
        await using (var db = _fixture.CreateContext(change))
        {
            id = (await Expenses(db, change).CreateAsync(new NewExpense(
                companyId, categoryId, new DateOnly(2026, 7, 17), null, "Stationery", 200m, 0m, null, 200m))).Id;
            rowVersion = await db.Expenses.Where(e => e.Id == id).Select(e => e.RowVersion).SingleAsync();
        }

        await using (var db = _fixture.CreateContext(change))
        {
            await Expenses(db, change).VoidAsync(id, rowVersion);
        }

        await using (var db = _fixture.CreateContext(change))
        {
            (await db.Expenses.CountAsync(e => e.Id == id)).Should().Be(0);
            (await db.Expenses.IgnoreQueryFilters().CountAsync(e => e.Id == id && e.DeletedAt != null)).Should().Be(1);
        }
    }

    [Fact]
    public async Task Recording_a_vat_expense_posts_the_general_ledger_entry()
    {
        var (companyId, categoryId) = await SeedCompanyAndCategory();
        var change = new FakeChangeContext { UserId = 1, CompanyId = companyId };

        long id;
        await using (var db = _fixture.CreateContext(change))
        {
            // Net 1000 + 5% VAT (50) = 1050, owed — not yet paid.
            id = (await Expenses(db, change).CreateAsync(new NewExpense(
                companyId, categoryId, new DateOnly(2026, 7, 17), "SUP-1", "Fuel", 1000m, 5m, "100123456700003", 1050m))).Id;
        }

        await using (var db = _fixture.CreateContext(change))
        {
            var entry = await db.GlEntries
                .Include(e => e.Lines)
                .SingleAsync(e => e.SourceType == GlSources.Expense && e.SourceId == id);

            entry.CompanyId.Should().Be(companyId);
            entry.Lines.Sum(l => l.Debit).Should().Be(entry.Lines.Sum(l => l.Credit)); // balances
            entry.Lines.Sum(l => l.Debit).Should().Be(1050m);

            // Dr the category's expense account (net), Dr Input VAT, Cr Accounts Payable — incurred, not paid.
            (await Debit(db, entry.Id, GlAccountCodes.ExpenseCategory(categoryId))).Should().Be(1000m);
            (await Debit(db, entry.Id, GlAccountCodes.InputVat)).Should().Be(50m);
            (await Credit(db, entry.Id, GlAccountCodes.AccountsPayable)).Should().Be(1050m);
        }
    }

    [Fact]
    public async Task A_vat_expense_keeps_the_vat_number_off_the_bill_without_a_supplier_link()
    {
        var (companyId, categoryId) = await SeedCompanyAndCategory();
        var change = new FakeChangeContext { UserId = 1, CompanyId = companyId };

        long withVat;
        long withoutVat;
        await using (var db = _fixture.CreateContext(change))
        {
            var service = Expenses(db, change);

            // Typed in off the vendor's tax invoice — an expense has no supplier to look one up from.
            withVat = (await service.CreateAsync(new NewExpense(
                companyId, categoryId, new DateOnly(2026, 7, 17), "SUP-2", "Courier", 100m, 5m, "  100123456700003  ", 105m))).Id;

            // Not a VAT expense: nothing to record, and a blank is stored as nothing rather than as "".
            withoutVat = (await service.CreateAsync(new NewExpense(
                companyId, categoryId, new DateOnly(2026, 7, 17), null, "Parking", 20m, 0m, "   ", 20m))).Id;
        }

        await using (var db = _fixture.CreateContext(change))
        {
            (await db.Expenses.SingleAsync(e => e.Id == withVat)).VatNumber.Should().Be("100123456700003");
            (await db.Expenses.SingleAsync(e => e.Id == withoutVat)).VatNumber.Should().BeNull();
        }
    }

    [Fact]
    public async Task An_expense_settles_over_several_payments_and_the_outstanding_is_derived()
    {
        var (companyId, categoryId) = await SeedCompanyAndCategory();
        var change = new FakeChangeContext { UserId = 1, CompanyId = companyId };

        long id;
        await using (var db = _fixture.CreateContext(change))
        {
            id = (await Expenses(db, change).CreateAsync(new NewExpense(
                companyId, categoryId, new DateOnly(2026, 7, 17), "SUP-3", "Annual licence", 5000m, 0m, null, 5000m))).Id;
        }

        long firstPayment;
        await using (var db = _fixture.CreateContext(change))
        {
            var recorded = await Payments(db, change).RecordPaymentAsync(
                id, new RecordExpensePayment(2000m, new DateOnly(2026, 7, 20), "Bank", "TRF-1"));

            recorded.Outstanding.Should().Be(3000m);
            firstPayment = recorded.PaymentId;
        }

        await using (var db = _fixture.CreateContext(change))
        {
            var recorded = await Payments(db, change).RecordPaymentAsync(
                id, new RecordExpensePayment(3000m, new DateOnly(2026, 7, 30), "Cash", "R-7"));

            recorded.Outstanding.Should().Be(0m); // settled — a derived fact, not a flag
        }

        await using (var db = _fixture.CreateContext(change))
        {
            (await db.ExpensePayments.Where(p => p.ExpenseId == id).SumAsync(p => p.Amount)).Should().Be(5000m);

            // The legacy columns mirror the latest settlement, so the surviving ExpenseReport still reads one.
            var expense = await db.Expenses.SingleAsync(e => e.Id == id);
            expense.Method.Should().Be("Cash");
            expense.Reference.Should().Be("R-7");

            // Each payment settles the payable the expense raised: Dr Accounts Payable, Cr Bank/Cash.
            var entry = await db.GlEntries
                .Include(e => e.Lines)
                .SingleAsync(e => e.SourceType == GlSources.ExpensePayment && e.SourceId == firstPayment);
            (await Debit(db, entry.Id, GlAccountCodes.AccountsPayable)).Should().Be(2000m);
            (await Credit(db, entry.Id, GlAccountCodes.Bank)).Should().Be(2000m);
        }
    }

    [Fact]
    public async Task A_payment_for_more_than_is_outstanding_is_refused()
    {
        var (companyId, categoryId) = await SeedCompanyAndCategory();
        var change = new FakeChangeContext { UserId = 1, CompanyId = companyId };

        long id;
        await using (var db = _fixture.CreateContext(change))
        {
            id = (await Expenses(db, change).CreateAsync(new NewExpense(
                companyId, categoryId, new DateOnly(2026, 7, 17), null, "Courier", 300m, 0m, null, 300m))).Id;
            await Payments(db, change).RecordPaymentAsync(id, new RecordExpensePayment(100m, new DateOnly(2026, 7, 18), "Cash", null));
        }

        await using (var db = _fixture.CreateContext(change))
        {
            var act = () => Payments(db, change).RecordPaymentAsync(
                id, new RecordExpensePayment(250m, new DateOnly(2026, 7, 19), "Cash", null));

            (await act.Should().ThrowAsync<ExpensePaymentExceedsOutstandingException>())
                .Which.Outstanding.Should().Be(200m);
        }
    }

    [Fact]
    public async Task A_settled_expense_cannot_be_voided_until_its_payments_are()
    {
        var (companyId, categoryId) = await SeedCompanyAndCategory();
        var change = new FakeChangeContext { UserId = 1, CompanyId = companyId };

        long id;
        long paymentId;
        await using (var db = _fixture.CreateContext(change))
        {
            id = (await Expenses(db, change).CreateAsync(new NewExpense(
                companyId, categoryId, new DateOnly(2026, 7, 17), null, "Cleaning", 400m, 0m, null, 400m))).Id;
            paymentId = (await Payments(db, change).RecordPaymentAsync(
                id, new RecordExpensePayment(400m, new DateOnly(2026, 7, 18), "Cash", "R-8"))).PaymentId;
        }

        await using (var db = _fixture.CreateContext(change))
        {
            var rowVersion = await db.Expenses.Where(e => e.Id == id).Select(e => e.RowVersion).SingleAsync();
            var act = () => Expenses(db, change).VoidAsync(id, rowVersion);

            await act.Should().ThrowAsync<ExpenseHasPaymentsException>();
        }

        await using (var db = _fixture.CreateContext(change))
        {
            var rowVersion = await db.ExpensePayments.Where(p => p.Id == paymentId).Select(p => p.RowVersion).SingleAsync();
            await Payments(db, change).VoidPaymentAsync(paymentId, rowVersion);
        }

        await using (var db = _fixture.CreateContext(change))
        {
            // The money came back — Dr Cash, Cr Accounts Payable — and the expense owes again.
            var entry = await db.GlEntries.SingleAsync(e => e.SourceType == GlSources.ExpensePaymentVoid && e.SourceId == paymentId);
            (await Debit(db, entry.Id, GlAccountCodes.Cash)).Should().Be(400m);
            (await Credit(db, entry.Id, GlAccountCodes.AccountsPayable)).Should().Be(400m);

            (await db.ExpensePayments.CountAsync(p => p.ExpenseId == id)).Should().Be(0);

            // The mirrored legacy method/reference went with it — nothing is settled any more.
            var expense = await db.Expenses.SingleAsync(e => e.Id == id);
            expense.Method.Should().BeEmpty();

            await Expenses(db, change).VoidAsync(id, expense.RowVersion);
        }

        await using (var db = _fixture.CreateContext(change))
        {
            (await db.Expenses.CountAsync(e => e.Id == id)).Should().Be(0);
        }
    }

    [Fact]
    public async Task Settling_an_expense_by_cheque_raises_a_cheque_for_the_payment()
    {
        var (companyId, categoryId) = await SeedCompanyAndCategory();
        var change = new FakeChangeContext { UserId = 1, CompanyId = companyId };

        long paymentId;
        await using (var db = _fixture.CreateContext(change))
        {
            var id = (await Expenses(db, change).CreateAsync(new NewExpense(
                companyId, categoryId, new DateOnly(2026, 7, 17), null, "Office rent", 9000m, 0m, null, 9000m))).Id;

            paymentId = (await Payments(db, change).RecordPaymentAsync(id, new RecordExpensePayment(
                9000m, new DateOnly(2026, 7, 25), "Cheque", "100234",
                ChequePayee: "Al Nakheel Real Estate", ChequeBank: "ENBD", ChequeNumber: "100234",
                ChequeDueDate: new DateOnly(2026, 8, 1)))).PaymentId;
        }

        await using (var db = _fixture.CreateContext(change))
        {
            var cheque = await db.Cheques.SingleAsync(c => c.SourceType == ChequeSource.ExpensePayment && c.SourceId == paymentId);
            cheque.PayTo.Should().Be("Al Nakheel Real Estate");
            cheque.Amount.Should().Be(9000m);
            cheque.DueDate.Should().Be(new DateOnly(2026, 8, 1));
        }
    }

    private static ExpenseService Expenses(SmartnetDbContext db, FakeChangeContext change) =>
        new(db, new GeneralLedger(db), change, Clock);

    private static ExpensePaymentService Payments(SmartnetDbContext db, FakeChangeContext change) =>
        new(db, new ChequeService(db, change, Clock), new GeneralLedger(db), change, Clock);

    private static async Task<decimal> Debit(SmartnetDbContext db, long entryId, string code) =>
        await db.GlLines
            .Where(l => l.GlEntryId == entryId && db.GlAccounts.Any(a => a.Id == l.AccountId && a.Code == code))
            .Select(l => l.Debit)
            .SingleAsync();

    private static async Task<decimal> Credit(SmartnetDbContext db, long entryId, string code) =>
        await db.GlLines
            .Where(l => l.GlEntryId == entryId && db.GlAccounts.Any(a => a.Id == l.AccountId && a.Code == code))
            .Select(l => l.Credit)
            .SingleAsync();

    private async Task<(long CompanyId, long CategoryId)> SeedCompanyAndCategory()
    {
        await using var db = _fixture.CreateContext(new FakeChangeContext { UserId = 1 });
        var company = new Company { Name = "Smart Net (test)", VatCode = "1", IsVatRegistered = true };
        db.Companies.Add(company);
        var category = new ExpenseCategory { Name = $"Fuel-{Guid.NewGuid():N}"[..12] };
        db.ExpenseCategories.Add(category);
        await db.SaveChangesAsync();
        return (company.Id, category.Id);
    }

    private sealed record ExpenseShadow(string? ExpCat, string? ExpenseAmount, string? Company);
}
