using FluentAssertions;
using Smartnet.Api.Reporting;
using Smartnet.Domain.Reporting;
using Smartnet.Infrastructure.Entities;

namespace Smartnet.Tests.Reporting;

public sealed class ExpenseReportTests
{
    private static readonly IReadOnlyDictionary<int, string> Categories =
        new Dictionary<int, string> { [3] = "Fuel", [7] = "Rent" };

    private static ExpenseTr Expense(int id, string category, string amount, string date = "2026-07-15") =>
        new()
        {
            Id = id,
            ExpCat = category,
            ExpenseAmount = amount,
            ExpenseDate = date,
            ExpenseDesc = "desc",
            Paymentm = "Cash",
            PaymentRef = "ref",
            Addedby = "Nimal",
            Addeddt = "2026-07-15",
            Company = "1",
        };

    [Fact]
    public void It_totals_amounts_and_resolves_category_names()
    {
        var expenses = new[] { Expense(1, "3", "500"), Expense(2, "7", "1500") };

        var report = ExpenseReport.Build(expenses, Categories, ReportPeriod.All);

        report.Total.Should().Be(2000m);
        report.Count.Should().Be(2);
        report.Rows.Select(r => r.Category).Should().Contain("Fuel").And.Contain("Rent");
    }

    [Fact]
    public void An_unknown_category_id_falls_back_to_the_raw_value_rather_than_blank()
    {
        var report = ExpenseReport.Build([Expense(1, "99", "10")], Categories, ReportPeriod.All);

        report.Rows[0].Category.Should().Be("99");
    }

    [Fact]
    public void A_non_numeric_amount_flags_the_row_and_never_throws()
    {
        var report = ExpenseReport.Build([Expense(1, "3", "—")], Categories, ReportPeriod.All);

        report.Rows[0].Amount.Should().Be(0m);
        report.Rows[0].HasDataIssue.Should().BeTrue();
        report.FlaggedCount.Should().Be(1);
    }

    [Fact]
    public void The_period_filters_by_expense_date()
    {
        var expenses = new[]
        {
            Expense(1, "3", "10", date: "2026-07-10"),
            Expense(2, "3", "10", date: "2026-08-10"),
        };

        var july = new ReportPeriod(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31));
        var report = ExpenseReport.Build(expenses, Categories, july);

        report.Count.Should().Be(1);
        report.Rows[0].Id.Should().Be(1);
    }

    [Fact]
    public void Rows_are_ordered_newest_first()
    {
        var expenses = new[]
        {
            Expense(1, "3", "10", date: "2026-07-01"),
            Expense(2, "3", "10", date: "2026-07-20"),
        };

        var report = ExpenseReport.Build(expenses, Categories, ReportPeriod.All);

        report.Rows.Select(r => r.Id).Should().ContainInOrder(2, 1);
    }
}
