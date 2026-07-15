using FluentAssertions;
using Smartnet.Api.Reporting;
using Smartnet.Infrastructure.Entities;

namespace Smartnet.Tests.Reporting;

public sealed class OutstandingReportTests
{
    private static readonly DateOnly AsOf = new(2026, 7, 31);

    private static readonly IReadOnlyDictionary<string, string> Names =
        new Dictionary<string, string>(StringComparer.Ordinal) { ["C-1"] = "Kandy", ["C-2"] = "Colombo" };

    private static InvoiceH Inv(string customer, string balance, string date) =>
        new() { Customer = customer, Balance = balance, Indate = date, Invoiceno = "SNI-1" };

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

        var report = OutstandingReport.Build(invoices, Names, AsOf);

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

        var report = OutstandingReport.Build(invoices, Names, AsOf);

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

        var report = OutstandingReport.Build(invoices, Names, AsOf);

        report.Rows.Should().ContainSingle();
        report.Rows[0].CustomerCode.Should().Be("C-2");
        report.Rows[0].Outstanding.Should().Be(0m);
        report.Rows[0].HasDefect.Should().BeTrue();
    }
}
