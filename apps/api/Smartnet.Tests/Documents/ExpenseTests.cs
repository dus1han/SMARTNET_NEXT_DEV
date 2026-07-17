using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Smartnet.Domain.Documents;
using Smartnet.Domain.MasterData;
using Smartnet.Domain.Settings;
using Smartnet.Infrastructure.Documents;
using Smartnet.Tests.Auditing;

namespace Smartnet.Tests.Documents;

/// <summary>
/// Expenses &amp; categories (Phase 7, slice 3): a flat adopted log. An expense persists its typed columns and
/// dual-writes the legacy <c>expense_tr</c> varchars so the surviving <c>ExpenseReport</c> reads a whole row.
/// Void is soft, not the legacy hard delete.
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
            created = await new ExpenseService(db, new ChequeService(db, change, Clock), change, Clock).CreateAsync(new NewExpense(
                companyId, categoryId, new DateOnly(2026, 7, 17), null, "Petrol", 5000m, 0m, 5000m, "Cash", "R-9"));
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
        var act = () => new ExpenseService(db, new ChequeService(db, change, Clock), change, Clock).CreateAsync(new NewExpense(
            companyId, CategoryId: 999999, new DateOnly(2026, 7, 17), null, "Bad", 10m, 0m, 10m, "Cash", null));

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
            id = (await new ExpenseService(db, new ChequeService(db, change, Clock), change, Clock).CreateAsync(new NewExpense(
                companyId, categoryId, new DateOnly(2026, 7, 17), null, "Stationery", 200m, 0m, 200m, "Cash", null))).Id;
            rowVersion = await db.Expenses.Where(e => e.Id == id).Select(e => e.RowVersion).SingleAsync();
        }

        await using (var db = _fixture.CreateContext(change))
        {
            await new ExpenseService(db, new ChequeService(db, change, Clock), change, Clock).VoidAsync(id, rowVersion);
        }

        await using (var db = _fixture.CreateContext(change))
        {
            (await db.Expenses.CountAsync(e => e.Id == id)).Should().Be(0);
            (await db.Expenses.IgnoreQueryFilters().CountAsync(e => e.Id == id && e.DeletedAt != null)).Should().Be(1);
        }
    }

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
