using FluentAssertions;
using Smartnet.Api.Reporting;
using Smartnet.Domain.Reporting;
using Smartnet.Infrastructure.Entities;

namespace Smartnet.Tests.Reporting;

public sealed class JobCardReportTests
{
    private static readonly IReadOnlyDictionary<string, string> Names =
        new Dictionary<string, string>(StringComparer.Ordinal) { ["C-1"] = "Kandy" };

    private static JobsM Job(string status, string cost, string sell, string date = "2026-07-15", string customer = "C-1") =>
        new()
        {
            Jstat = status,
            Cost = cost,
            Sell = sell,
            Jdate = date,
            Customer = customer,
            Jobno = "J-1",
            Company = "1",
            Jobdoneby = "Tech",
            Completedby = "Tech",
        };

    [Fact]
    public void A_closed_job_shows_profit_as_sell_minus_cost()
    {
        var report = JobCardReport.Build([Job("CLOSED", cost: "100", sell: "300")], Names, ReportPeriod.All);

        report.Rows[0].Profit.Should().Be(200m);
        report.Rows[0].CustomerName.Should().Be("Kandy");
        report.TotalCost.Should().Be(100m);
        report.TotalSell.Should().Be(300m);
        report.TotalProfit.Should().Be(200m);
    }

    [Fact]
    public void A_pending_job_has_no_profit_and_is_not_flagged_for_its_blank_figures()
    {
        // Pending jobs have no cost/sell yet — blank, not a defect; profit is null, not zero.
        var report = JobCardReport.Build([Job("PENDING", cost: "", sell: "")], Names, ReportPeriod.All);

        report.Rows[0].Profit.Should().BeNull();
        report.Rows[0].Cost.Should().Be(0m);
        report.Rows[0].HasDataIssue.Should().BeFalse();
        report.TotalProfit.Should().Be(0m);
    }

    [Fact]
    public void A_completed_job_with_a_non_numeric_figure_is_flagged()
    {
        var report = JobCardReport.Build([Job("CLOSED", cost: "oops", sell: "300")], Names, ReportPeriod.All);

        report.Rows[0].HasDataIssue.Should().BeTrue();
        report.Rows[0].Profit.Should().Be(300m); // 300 - 0
    }

    [Fact]
    public void It_filters_by_job_date()
    {
        var jobs = new[] { Job("CLOSED", "1", "2", "2026-07-10"), Job("CLOSED", "1", "2", "2026-08-10") };
        var july = new ReportPeriod(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31));

        JobCardReport.Build(jobs, Names, july).Count.Should().Be(1);
    }
}
