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

    private static readonly IReadOnlyDictionary<string, string> SupplierNames =
        new Dictionary<string, string>(StringComparer.Ordinal) { ["S-1"] = "Better Choice" };

    private static InvoiceH Invoice(string date, string total, string cost = "0", string balance = "0", string customer = "C-1") =>
        new() { Invoiceno = $"I-{date}-{total}", Indate = date, Totamount = total, Cost = cost, Balance = balance, Customer = customer };

    private static Payment Pay(string date, string amount) =>
        new() { Paymentrecdate = date, Amount = amount, Invoiceno = "I-x" };

    private static DashboardAnalytics Build(
        IEnumerable<InvoiceH>? invoices = null,
        IEnumerable<Payment>? payments = null,
        IEnumerable<InvoiceL>? lines = null,
        IEnumerable<(DateOnly?, decimal)>? supplierPayments = null,
        IEnumerable<(string, decimal)>? supplierSpend = null,
        IReadOnlyDictionary<string, decimal>? creditLimits = null) =>
        DashboardAnalyticsBuilder.Build(
            (invoices ?? []).ToList(),
            (lines ?? []).ToList(),
            (payments ?? []).ToList(),
            [],
            (supplierPayments ?? []).ToList(),
            (supplierSpend ?? []).ToList(),
            Names,
            SupplierNames,
            creditLimits ?? new Dictionary<string, decimal>(StringComparer.Ordinal),
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
    public void Top_customers_covers_this_month_only()
    {
        // Scoped to the month deliberately: an all-time ranking is dominated by whoever was big years
        // ago and never changes, which makes it decoration rather than a reading.
        var report = Build([
            Invoice("2026-07-02", "100", customer: "C-2"),
            Invoice("2026-01-02", "9000", customer: "C-1"),
        ]);

        report.TopCustomers.Should().ContainSingle();
        report.TopCustomers[0].Name.Should().Be("Astron");
        report.TopCustomers[0].Share.Should().Be(100m, "the January sale is not this month's revenue");
    }

    [Fact]
    public void Days_to_collect_measures_invoice_to_last_payment()
    {
        var report = Build(
            invoices: [Invoice("2026-07-01", "1000")],
            payments: [new Payment { Invoiceno = "I-2026-07-01-1000", Amount = "1000", Paymentrecdate = "2026-07-11" }]);

        report.DaysToCollect.Should().Be(10);
    }

    [Fact]
    public void Days_to_collect_is_unknown_rather_than_zero_when_nothing_is_settled()
    {
        // Zero would read as "customers pay immediately" — the opposite of what no data means.
        var report = Build([Invoice("2026-07-01", "1000", balance: "1000")]);

        report.DaysToCollect.Should().BeNull();
    }

    [Fact]
    public void The_mix_splits_the_month_between_cash_and_credit()
    {
        var report = DashboardAnalyticsBuilder.Build(
            [
                new InvoiceH { Invoiceno = "A", Indate = "2026-07-02", Totamount = "300", Cost = "0", Balance = "0", Customer = "C-1", Invtype = "Cash" },
                new InvoiceH { Invoiceno = "B", Indate = "2026-07-03", Totamount = "700", Cost = "0", Balance = "700", Customer = "C-1", Invtype = "Credit" },
            ],
            [], [], [], [], [], Names, SupplierNames, new Dictionary<string, decimal>(StringComparer.Ordinal), Today);

        report.Mix.Cash.Should().Be(300m);
        report.Mix.Credit.Should().Be(700m);
        report.Mix.CashCount.Should().Be(1);
        report.InvoiceCount.Should().Be(2);
        report.AverageInvoice.Should().Be(500m);
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
    public void Overdue_by_customer_names_who_is_late_and_how_late()
    {
        var report = Build([
            Invoice("2026-07-15", "500", balance: "500", customer: "C-2"),   // current, not chased
            Invoice("2025-05-01", "900", balance: "900", customer: "C-1"),   // long overdue
            Invoice("2026-05-20", "100", balance: "100", customer: "C-1"),
        ]);

        report.OverdueByCustomer.Should().ContainSingle("only C-1 is past the current bucket");
        var debt = report.OverdueByCustomer[0];
        debt.Name.Should().Be("Vogue Tex");
        debt.Owed.Should().Be(1000m);
        debt.Invoices.Should().Be(2);
        debt.OldestDays.Should().BeGreaterThan(400, "the oldest invoice sets this, not the average");
    }

    [Fact]
    public void Supplier_spend_is_ranked_all_time_with_its_share()
    {
        var report = Build(supplierSpend: [("S-1", 750m), ("S-2", 250m)]);

        report.TopSuppliers[0].Name.Should().Be("Better Choice");
        report.TopSuppliers[0].Share.Should().Be(75m);
        report.TopSuppliers[1].Name.Should().Be("S-2", "an unknown code falls back to the code itself");
    }

    [Fact]
    public void New_customers_counts_only_a_first_ever_invoice()
    {
        var report = Build([
            Invoice("2026-07-05", "100", customer: "C-1"),
            Invoice("2025-03-05", "100", customer: "C-1"),   // C-1 is not new — it bought last year
            Invoice("2026-07-06", "100", customer: "C-2"),   // C-2 is new
        ]);

        report.NewCustomers.Value.Should().Be(1);
    }

    [Fact]
    public void A_customer_over_their_limit_is_listed_with_the_overrun()
    {
        var report = Build(
            [Invoice("2026-07-01", "800", balance: "800", customer: "C-1")],
            creditLimits: new Dictionary<string, decimal>(StringComparer.Ordinal) { ["C-1"] = 500m });

        report.OverCreditLimit.Should().ContainSingle();
        report.OverCreditLimit[0].Name.Should().Be("Vogue Tex");
        report.OverCreditLimit[0].Excess.Should().Be(300m);
    }

    [Fact]
    public void A_customer_with_no_limit_recorded_cannot_breach_one()
    {
        // 198 of the 223 customers have no limit set. Reading those as a limit of nil would report every
        // one of them as a breach and bury the twenty-five that are real.
        var report = Build([Invoice("2026-07-01", "800", balance: "800", customer: "C-1")]);

        report.OverCreditLimit.Should().BeEmpty();
    }

    [Fact]
    public void Lapsed_customers_are_those_silent_ninety_days_but_not_gone_two_years()
    {
        var report = Build([
            Invoice("2026-07-01", "100", customer: "C-1"),   // bought this month — not lapsed
            Invoice("2026-01-05", "900", customer: "C-2", balance: "400"),  // ~6 months silent
        ]);

        report.LapsedCount.Should().Be(1);
        report.LapsedCustomers.Should().ContainSingle();
        report.LapsedCustomers[0].Name.Should().Be("Astron");
        report.LapsedCustomers[0].Lifetime.Should().Be(900m);
        report.LapsedCustomers[0].StillOwed.Should().Be(400m, "gone and still owing is the urgent case");
    }

    [Fact]
    public void A_customer_gone_more_than_two_years_has_left_rather_than_lapsed()
    {
        var report = Build([Invoice("2023-01-05", "900", customer: "C-2")]);

        report.LapsedCount.Should().Be(0);
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
