using FluentAssertions;
using Smartnet.Api.Reporting;
using Smartnet.Domain.Reporting;
using Smartnet.Infrastructure.Entities;

namespace Smartnet.Tests.Reporting;

public sealed class SalesReportTests
{
    private static readonly IReadOnlyDictionary<string, string> Names =
        new Dictionary<string, string>(StringComparer.Ordinal) { ["C-1"] = "Kandy Engineering" };

    private static InvoiceH Invoice(
        string invoiceNo,
        string type,
        string total,
        string cost,
        string date = "2026-07-15",
        string customer = "C-1",
        string balance = "0") =>
        new()
        {
            It = "ITEM",
            Invoiceno = invoiceNo,
            Invtype = type,
            Indate = date,
            Customer = customer,
            Totamount = total,
            Cost = cost,
            Balance = balance,
        };

    [Fact]
    public void Summary_splits_cash_and_credit_and_totals_the_lot()
    {
        var invoices = new[]
        {
            Invoice("SNI-1", "Cash", total: "100", cost: "60"),
            Invoice("SNI-2", "Credit", total: "200", cost: "150"),
        };

        var report = SalesReport.Build(invoices, Names, ReportPeriod.All);

        report.Summary.CashSales.Should().Be(100m);
        report.Summary.CashProfit.Should().Be(40m);
        report.Summary.CreditSales.Should().Be(200m);
        report.Summary.CreditProfit.Should().Be(50m);
        // Total is every invoice, not Cash + Credit (though here they coincide).
        report.Summary.TotalSales.Should().Be(300m);
        report.Summary.TotalProfit.Should().Be(90m);
        report.Summary.InvoiceCount.Should().Be(2);
        report.Summary.FlaggedCount.Should().Be(0);
    }

    [Fact]
    public void Total_includes_an_invoice_whose_type_is_neither_cash_nor_credit()
    {
        var invoices = new[]
        {
            Invoice("SNI-1", "Cash", total: "100", cost: "0"),
            Invoice("SNI-2", "", total: "70", cost: "0"), // a legacy row with a blank invtype
        };

        var report = SalesReport.Build(invoices, Names, ReportPeriod.All);

        report.Summary.CashSales.Should().Be(100m);
        report.Summary.CreditSales.Should().Be(0m);
        report.Summary.TotalSales.Should().Be(170m); // the blank-type row still counts in the total
    }

    [Fact]
    public void A_non_numeric_amount_flags_the_row_and_is_treated_as_zero_never_thrown()
    {
        var invoices = new[] { Invoice("SNI-1", "Cash", total: "not-a-number", cost: "10") };

        var report = SalesReport.Build(invoices, Names, ReportPeriod.All);

        report.Rows.Should().ContainSingle();
        report.Rows[0].Total.Should().Be(0m);
        report.Rows[0].HasDataIssue.Should().BeTrue();
        report.Summary.FlaggedCount.Should().Be(1);
    }

    [Fact]
    public void The_period_filters_by_indate()
    {
        var invoices = new[]
        {
            Invoice("SNI-1", "Cash", "100", "0", date: "2026-07-10"),
            Invoice("SNI-2", "Cash", "100", "0", date: "2026-08-10"),
        };

        var july = new ReportPeriod(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31));
        var report = SalesReport.Build(invoices, Names, july);

        report.Rows.Should().ContainSingle();
        report.Rows[0].InvoiceNo.Should().Be("SNI-1");
    }

    [Fact]
    public void A_missing_customer_keeps_the_invoice_with_a_blank_name()
    {
        // The legacy detail inner-joins cus_m and would drop this row; we keep it so the detail still
        // reconciles with the summary.
        var invoices = new[] { Invoice("SNI-1", "Cash", "100", "0", customer: "GONE") };

        var report = SalesReport.Build(invoices, Names, ReportPeriod.All);

        report.Rows.Should().ContainSingle();
        report.Rows[0].CustomerName.Should().BeEmpty();
        report.Summary.TotalSales.Should().Be(100m);
    }

    [Fact]
    public void Rows_are_ordered_by_the_numeric_suffix_of_the_invoice_number()
    {
        var invoices = new[]
        {
            Invoice("SNI-10", "Cash", "1", "0"),
            Invoice("SNI-2", "Cash", "1", "0"),
            Invoice("SNI-1", "Cash", "1", "0"),
        };

        var report = SalesReport.Build(invoices, Names, ReportPeriod.All);

        report.Rows.Select(r => r.InvoiceNo).Should().ContainInOrder("SNI-1", "SNI-2", "SNI-10");
    }
}
