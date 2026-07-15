using FluentAssertions;
using Smartnet.Api.Contracts;
using Smartnet.Api.Reporting;
using Smartnet.Infrastructure.Entities;

namespace Smartnet.Tests.Reporting;

public sealed class DashboardTests
{
    private static readonly DateOnly MonthStart = new(2026, 7, 1);
    private static readonly DateOnly MonthEnd = new(2026, 7, 31);

    private static InvoiceH Invoice(
        string type,
        string total,
        string cost = "0",
        string balance = "0",
        string date = "2026-07-15",
        string preparedBy = "Nimal") =>
        new()
        {
            It = "ITEM",
            Invtype = type,
            Totamount = total,
            Cost = cost,
            Balance = balance,
            Indate = date,
            Preparedby = preparedBy,
            Invoiceno = "SNI-1",
        };

    [Fact]
    public void The_company_view_totals_every_invoice_and_labels_itself()
    {
        var invoices = new[]
        {
            Invoice("Cash", total: "100", cost: "60"),
            Invoice("Credit", total: "200", cost: "150", preparedBy: "Someone Else"),
        };

        var dashboard = Dashboard.Build(invoices, MonthStart, MonthEnd, preparedBy: null);

        dashboard.View.Should().Be("company");
        dashboard.Approximate.Should().BeFalse();
        dashboard.CashSales.Should().Be(100m);
        dashboard.CreditSales.Should().Be(200m);
        dashboard.TotalSales.Should().Be(300m);
        dashboard.Profit.Should().Be(90m); // (100-60) + (200-150)
    }

    [Fact]
    public void The_my_view_scopes_to_one_preparer_and_is_flagged_approximate()
    {
        var invoices = new[]
        {
            Invoice("Cash", total: "100", preparedBy: "Nimal"),
            Invoice("Cash", total: "500", preparedBy: "Someone Else"),
        };

        var dashboard = Dashboard.Build(invoices, MonthStart, MonthEnd, preparedBy: "Nimal");

        dashboard.View.Should().Be("my");
        dashboard.Approximate.Should().BeTrue();
        dashboard.CashSales.Should().Be(100m); // only Nimal's
        dashboard.TotalSales.Should().Be(100m);
    }

    [Fact]
    public void Outstanding_is_the_whole_scoped_balance_all_time_not_just_the_month()
    {
        var invoices = new[]
        {
            Invoice("Credit", total: "200", balance: "200", date: "2026-07-10"),
            Invoice("Credit", total: "300", balance: "300", date: "2025-01-01"), // outside the month
        };

        var dashboard = Dashboard.Build(invoices, MonthStart, MonthEnd, preparedBy: null);

        // Sales are month-scoped; outstanding is not.
        dashboard.TotalSales.Should().Be(200m);
        dashboard.Outstanding.Should().Be(500m);
    }

    [Fact]
    public void My_view_outstanding_is_scoped_to_the_preparer_unlike_the_legacy_dashboard()
    {
        // The legacy UserDashboard showed company-wide payments/outstanding under "mine"; here "mine"
        // means mine.
        var invoices = new[]
        {
            Invoice("Credit", total: "200", balance: "200", preparedBy: "Nimal"),
            Invoice("Credit", total: "900", balance: "900", preparedBy: "Someone Else"),
        };

        var dashboard = Dashboard.Build(invoices, MonthStart, MonthEnd, preparedBy: "Nimal");

        dashboard.Outstanding.Should().Be(200m);
    }

    [Fact]
    public void The_chart_has_a_point_for_every_day_of_the_month()
    {
        var dashboard = Dashboard.Build([], MonthStart, MonthEnd, preparedBy: null);

        dashboard.Chart.Should().HaveCount(31);
        dashboard.Chart[0].Date.Should().Be(MonthStart);
        dashboard.Chart[^1].Date.Should().Be(MonthEnd);
        dashboard.Chart.Should().OnlyContain(p => p.Cash == 0m && p.Credit == 0m);
    }

    [Fact]
    public void The_chart_splits_cash_and_credit_on_the_day_they_fall()
    {
        var invoices = new[]
        {
            Invoice("Cash", total: "100", date: "2026-07-10"),
            Invoice("Credit", total: "250", date: "2026-07-10"),
            Invoice("Cash", total: "40", date: "2026-07-20"),
        };

        var dashboard = Dashboard.Build(invoices, MonthStart, MonthEnd, preparedBy: null);

        var tenth = dashboard.Chart.Single(p => p.Date == new DateOnly(2026, 7, 10));
        tenth.Cash.Should().Be(100m);
        tenth.Credit.Should().Be(250m);

        var twentieth = dashboard.Chart.Single(p => p.Date == new DateOnly(2026, 7, 20));
        twentieth.Cash.Should().Be(40m);
        twentieth.Credit.Should().Be(0m);
    }

    [Fact]
    public void A_non_numeric_amount_is_counted_zero_and_flagged_never_thrown()
    {
        var dashboard = Dashboard.Build(
            [Invoice("Cash", total: "not-a-number")], MonthStart, MonthEnd, preparedBy: null);

        dashboard.CashSales.Should().Be(0m);
        dashboard.FlaggedCount.Should().Be(1);
    }

    [Fact]
    public void It_carries_the_selected_company_and_the_options_for_the_in_card_selector()
    {
        DashboardCompanyOption[] companies =
            [new(1, "Smart Technologies"), new(2, "Smart Net")];

        var scoped = Dashboard.Build([], MonthStart, MonthEnd, preparedBy: null, selectedCompanyId: 2, companies: companies);
        scoped.SelectedCompanyId.Should().Be(2);
        scoped.Companies.Should().HaveCount(2);

        // "All" (the default) carries no selected id and, here, no options.
        var all = Dashboard.Build([], MonthStart, MonthEnd, preparedBy: null);
        all.SelectedCompanyId.Should().BeNull();
        all.Companies.Should().BeEmpty();
    }
}
