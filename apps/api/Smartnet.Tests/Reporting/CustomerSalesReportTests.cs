using FluentAssertions;
using Smartnet.Api.Reporting;
using Smartnet.Domain.Reporting;
using Smartnet.Infrastructure.Entities;

namespace Smartnet.Tests.Reporting;

public sealed class CustomerSalesReportTests
{
    private static readonly IReadOnlyDictionary<string, string> Names =
        new Dictionary<string, string>(StringComparer.Ordinal) { ["C-1"] = "Kandy", ["C-2"] = "Colombo" };

    private static InvoiceH Inv(string customer, string total, string cost, string balance = "0", string date = "2026-07-15") =>
        new()
        {
            Customer = customer,
            Totamount = total,
            Cost = cost,
            Balance = balance,
            Indate = date,
            Invoiceno = "SNI-1",
            Invtype = "Cash",
            It = "ITEM",
        };

    [Fact]
    public void It_groups_by_customer_and_ranks_by_profit()
    {
        var invoices = new[]
        {
            Inv("C-1", total: "300", cost: "100"), // C-1 profit 200
            Inv("C-1", total: "100", cost: "50"),  // + profit 50  → 250 total
            Inv("C-2", total: "500", cost: "450"), // C-2 profit 50
        };

        var report = CustomerSalesReport.Build(invoices, Names, ReportPeriod.All);

        report.Rows.Should().HaveCount(2);
        report.Rows[0].CustomerName.Should().Be("Kandy"); // highest profit first
        report.Rows[0].InvoiceCount.Should().Be(2);
        report.Rows[0].Total.Should().Be(400m);
        report.Rows[0].Profit.Should().Be(250m);
        report.Rows[1].CustomerName.Should().Be("Colombo");

        report.TotalSales.Should().Be(900m);
        report.TotalProfit.Should().Be(300m);
        report.CustomerCount.Should().Be(2);
    }

    [Fact]
    public void It_filters_by_period_and_flags_a_bad_amount()
    {
        var invoices = new[]
        {
            Inv("C-1", total: "100", cost: "0", date: "2026-07-10"),
            Inv("C-1", total: "bad", cost: "0", date: "2026-07-20"),
            Inv("C-2", total: "999", cost: "0", date: "2026-08-10"), // outside July
        };

        var july = new ReportPeriod(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31));
        var report = CustomerSalesReport.Build(invoices, Names, july);

        report.Rows.Should().ContainSingle(); // only C-1's July invoices
        report.Rows[0].Total.Should().Be(100m); // "bad" counted as 0
        report.Rows[0].HasDataIssue.Should().BeTrue();
        report.FlaggedCount.Should().Be(1);
    }
}
