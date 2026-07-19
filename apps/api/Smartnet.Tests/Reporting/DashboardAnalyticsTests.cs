using FluentAssertions;
using Smartnet.Api.Contracts;
using Smartnet.Api.Reporting;
using Smartnet.Infrastructure.Entities;

namespace Smartnet.Tests.Reporting;

/// <summary>
/// The dashboard's analytical readings. Every figure here ends up on a screen somebody makes decisions
/// from, so each one is pinned rather than trusted.
/// </summary>
public sealed class DashboardAnalyticsTests
{
    private static readonly DateOnly Today = new(2026, 7, 19);

    private static readonly IReadOnlyDictionary<string, string> Names =
        new Dictionary<string, string>(StringComparer.Ordinal) { ["C-1"] = "Vogue Tex", ["C-2"] = "Astron" };

    private static InvoiceH Invoice(string date, string total, string cost = "0", string balance = "0", string customer = "C-1") =>
        new() { Invoiceno = $"I-{date}-{total}", Indate = date, Totamount = total, Cost = cost, Balance = balance, Customer = customer };

    private static Payment Pay(string date, string amount) =>
        new() { Paymentrecdate = date, Amount = amount, Invoiceno = "I-x" };

    private static DashboardAnalytics Build(
        IEnumerable<InvoiceH>? invoices = null,
        IEnumerable<Payment>? payments = null,
        IEnumerable<InvoiceL>? lines = null,
        IEnumerable<(DateOnly?, decimal)>? supplierPayments = null) =>
        DashboardAnalyticsBuilder.Build(
            (invoices ?? []).ToList(),
            (lines ?? []).ToList(),
            (payments ?? []).ToList(),
            [],
            (supplierPayments ?? []).ToList(),
            Names,
            Today);

    [Fact]
    public void Revenue_and_profit_compare_this_month_against_last()
    {
        var report = Build([
            Invoice("2026-07-05", "1000", cost: "600"),
            Invoice("2026-07-18", "500", cost: "300"),
            Invoice("2026-06-10", "1000", cost: "500"),
        ]);

        report.Revenue.Value.Should().Be(1500m);
        report.Revenue.Previous.Should().Be(1000m);
        report.Revenue.ChangePercent.Should().Be(50m);

        report.GrossProfit.Value.Should().Be(600m);   // 1500 sold, 900 cost
        report.MarginPercent.Should().Be(40m);
    }

    [Fact]
    public void A_month_with_no_predecessor_reports_no_change_rather_than_a_fake_one()
    {
        // Dividing by a zero base would give infinity or a spurious 100%; the tile says "no prior month".
        var report = Build([Invoice("2026-07-05", "1000")]);

        report.Revenue.Previous.Should().Be(0m);
        report.Revenue.ChangePercent.Should().BeNull();
    }

    [Fact]
    public void Ageing_buckets_the_open_balance_by_how_long_it_has_been_owed()
    {
        var report = Build([
            Invoice("2026-07-10", "1000", balance: "1000"),  //   9 days -> Current
            Invoice("2026-06-10", "1000", balance: "1000"),  //  39 days -> 31-60
            Invoice("2026-05-10", "1000", balance: "1000"),  //  70 days -> 61-90
            Invoice("2025-01-10", "1000", balance: "1000"),  // 555 days -> over 90
        ]);

        report.Ageing.Select(b => b.Amount).Should().Equal(1000m, 1000m, 1000m, 1000m);
        report.Ageing.Select(b => b.Invoices).Should().Equal(1, 1, 1, 1);
    }

    [Fact]
    public void A_settled_invoice_is_not_owed_and_does_not_age()
    {
        var report = Build([Invoice("2025-01-10", "1000", balance: "0")]);

        report.Ageing.Sum(b => b.Amount).Should().Be(0m);
        report.Overdue.Should().Be(0m);
    }

    [Fact]
    public void Overdue_is_everything_past_the_current_bucket()
    {
        var report = Build([
            Invoice("2026-07-10", "500", balance: "500"),    // current — not overdue
            Invoice("2026-05-10", "300", balance: "300"),    // 61-90
            Invoice("2025-01-10", "200", balance: "200"),    // over 90
        ]);

        report.Overdue.Should().Be(500m, "the current bucket is owed but not late");
    }

    [Fact]
    public void Cash_flow_nets_receipts_against_supplier_payments()
    {
        var report = Build(
            payments: [Pay("2026-07-05", "800")],
            supplierPayments: [(new DateOnly(2026, 7, 09), 300m)]);

        var july = report.CashFlow.Single(p => p.Month == new DateOnly(2026, 7, 1));

        july.In.Should().Be(800m);
        july.Out.Should().Be(300m);
    }

    [Fact]
    public void Customer_concentration_reports_the_share_the_top_names_hold()
    {
        var report = Build([
            Invoice("2026-07-01", "800", customer: "C-1"),
            Invoice("2026-07-02", "200", customer: "C-2"),
        ]);

        report.TopCustomers.Should().HaveCount(2);
        report.TopCustomers[0].Name.Should().Be("Vogue Tex", "codes are resolved to names");
        report.TopCustomers[0].Share.Should().Be(80m);
        report.TopCustomerShare.Should().Be(100m);
    }

    [Fact]
    public void A_value_that_will_not_parse_counts_as_zero_rather_than_throwing()
    {
        // The same posture as every other report over this data — 28 invoices carry an unreadable cost.
        var report = Build([Invoice("2026-07-05", "1000", cost: "not a number")]);

        report.Revenue.Value.Should().Be(1000m);
        report.GrossProfit.Value.Should().Be(1000m);
    }

    [Fact]
    public void The_trend_covers_twelve_months_and_keeps_empty_ones()
    {
        var report = Build([Invoice("2026-07-05", "1000")]);

        report.MonthlyTrend.Should().HaveCount(12);
        report.MonthlyTrend[^1].Month.Should().Be(new DateOnly(2026, 7, 1), "the newest month is last");
        report.MonthlyTrend[0].Month.Should().Be(new DateOnly(2025, 8, 1));
        // Empty months stay in, so a gap in trading reads as a gap rather than being closed up.
        report.MonthlyTrend.Count(p => p.Revenue == 0m).Should().Be(11);
    }

    [Fact]
    public void Empty_data_produces_an_empty_report_not_an_exception()
    {
        var report = Build();

        report.Revenue.Value.Should().Be(0m);
        report.MarginPercent.Should().Be(0m);
        report.TopCustomers.Should().BeEmpty();
        report.Ageing.Should().HaveCount(4);
    }
}
