using FluentAssertions;
using Smartnet.Api.Reporting;
using Smartnet.Infrastructure.Entities;

namespace Smartnet.Tests.Reporting;

public sealed class OutstandingReportTests
{
    private static readonly DateOnly AsOf = new(2026, 7, 31);

    /// <summary>No payments-after-as-of — the live report, nothing rolled back.</summary>
    private static readonly IReadOnlyDictionary<string, decimal> Empty =
        new Dictionary<string, decimal>(StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, string> Names =
        new Dictionary<string, string>(StringComparer.Ordinal) { ["C-1"] = "Kandy", ["C-2"] = "Colombo" };

    private static InvoiceH Inv(string customer, string balance, string date) =>
        new() { Customer = customer, Balance = balance, Indate = date, Invoiceno = "SNI-1" };

    [Fact]
    public void As_of_a_past_date_it_adds_back_later_payments_and_drops_later_invoices()
    {
        var invoices = new[]
        {
            // Settled now (balance 0), but 1,000 was paid AFTER the as-of date — so it was outstanding then.
            new InvoiceH { Customer = "C-1", Balance = "0", Indate = "2026-05-01", Invoiceno = "SNI-1" },
            // Issued AFTER the as-of date — it did not exist yet, so it is not on the report.
            new InvoiceH { Customer = "C-1", Balance = "300", Indate = "2026-07-25", Invoiceno = "SNI-2" },
            // A plain still-outstanding invoice issued before the date.
            new InvoiceH { Customer = "C-2", Balance = "200", Indate = "2026-06-15", Invoiceno = "SNI-3" },
        };

        var asAt = new DateOnly(2026, 7, 15);
        var paidAfter = new Dictionary<string, decimal>(StringComparer.Ordinal) { ["SNI-1"] = 1000m };

        var report = OutstandingReport.Build(invoices, Names, asAt, paidAfter);

        report.AsAt.Should().Be(asAt);

        // C-1 owed 1,000 as of 15 Jul (the payment came later); the 25 Jul invoice is excluded.
        var kandy = report.Rows.Single(r => r.CustomerCode == "C-1");
        kandy.Outstanding.Should().Be(1000m);
        kandy.InvoiceCount.Should().Be(1);

        report.Rows.Single(r => r.CustomerCode == "C-2").Outstanding.Should().Be(200m);
        report.TotalOutstanding.Should().Be(1200m);
    }

    [Fact]
    public void It_ages_the_outstanding_balance_into_buckets()
    {
        var invoices = new[]
        {
            Inv("C-1", balance: "1000", date: "2026-07-20"), // 11 days → current
            Inv("C-1", balance: "500", date: "2026-05-01"),  // 91 days → 90+
            Inv("C-1", balance: "0", date: "2026-07-01"),    // settled → ignored
            Inv("C-2", balance: "200", date: "2026-06-15"),  // 46 days → 31-60
        };

        var report = OutstandingReport.Build(invoices, Names, AsOf, Empty);

        report.Rows.Should().HaveCount(2);
        var kandy = report.Rows.Single(r => r.CustomerCode == "C-1");
        kandy.Outstanding.Should().Be(1500m);
        kandy.Current.Should().Be(1000m);
        kandy.Days90.Should().Be(500m);
        kandy.OldestDays.Should().Be(91);
        kandy.InvoiceCount.Should().Be(2);

        var colombo = report.Rows.Single(r => r.CustomerCode == "C-2");
        colombo.Days30.Should().Be(200m);

        report.TotalOutstanding.Should().Be(1700m);
        report.Rows[0].CustomerCode.Should().Be("C-1"); // ordered by outstanding desc
    }

    [Fact]
    public void A_negative_balance_invoice_flags_the_customer_and_is_excluded_from_the_total()
    {
        // Finding 1: the legacy `balance > 0` filter drops the -300, overstating the outstanding.
        var invoices = new[]
        {
            Inv("C-1", balance: "1000", date: "2026-07-20"),
            Inv("C-1", balance: "-300", date: "2026-07-10"),
        };

        var report = OutstandingReport.Build(invoices, Names, AsOf, Empty);

        report.Rows.Should().ContainSingle();
        report.Rows[0].Outstanding.Should().Be(1000m); // as the business sends it (the -300 is dropped)
        report.Rows[0].HasDefect.Should().BeTrue();
        report.DefectCount.Should().Be(1);
    }

    [Fact]
    public void A_customer_with_no_positive_balance_does_not_appear_unless_defective()
    {
        var invoices = new[]
        {
            Inv("C-1", balance: "0", date: "2026-07-20"),   // settled → no row
            Inv("C-2", balance: "-50", date: "2026-07-20"), // only a negative → shown, flagged
        };

        var report = OutstandingReport.Build(invoices, Names, AsOf, Empty);

        report.Rows.Should().ContainSingle();
        report.Rows[0].CustomerCode.Should().Be("C-2");
        report.Rows[0].Outstanding.Should().Be(0m);
        report.Rows[0].HasDefect.Should().BeTrue();
    }
}
