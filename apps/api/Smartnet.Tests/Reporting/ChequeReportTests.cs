using FluentAssertions;
using Smartnet.Api.Reporting;
using Smartnet.Domain.Reporting;
using Smartnet.Infrastructure.Entities;

namespace Smartnet.Tests.Reporting;

public sealed class ChequeReportTests
{
    private static Cheque Chq(int id, string amount, string date = "2026-07-15") =>
        new()
        {
            Id = id,
            Amount = amount,
            Chequedate = date,
            Duedate = "2026-08-01",
            Payto = "ABC Traders",
            Bank = "HNB",
            Chkno = "100234",
            Createdby = "Nimal",
            Createddt = "2026-07-15 09:00:00",
            Printeddt = "2026-07-15 09:05:00",
            Company = "1",
            Entry = string.Empty,
            Supcode = string.Empty,
        };

    [Fact]
    public void It_totals_and_derives_amount_in_words()
    {
        var report = ChequeReport.Build([Chq(1, "1250")], ReportPeriod.All);

        report.Total.Should().Be(1250m);
        report.Count.Should().Be(1);
        report.Rows[0].AmountInWords.Should().Be("ONE THOUSAND TWO HUNDRED FIFTY ONLY");
        report.Rows[0].HasDataIssue.Should().BeFalse();
    }

    [Fact]
    public void The_created_and_printed_fields_are_each_their_own_value()
    {
        // The legacy export collapsed these three into one cell; here they are distinct.
        var row = ChequeReport.Build([Chq(1, "10")], ReportPeriod.All).Rows[0];

        row.CreatedBy.Should().Be("Nimal");
        row.CreatedAt.Should().Be("2026-07-15 09:00:00");
        row.PrintedAt.Should().Be("2026-07-15 09:05:00");
    }

    [Fact]
    public void A_non_numeric_amount_is_flagged_and_never_thrown()
    {
        var report = ChequeReport.Build([Chq(1, "n/a")], ReportPeriod.All);

        report.Rows[0].Amount.Should().Be(0m);
        report.Rows[0].HasDataIssue.Should().BeTrue();
        report.FlaggedCount.Should().Be(1);
    }

    [Fact]
    public void It_filters_by_cheque_date()
    {
        var cheques = new[] { Chq(1, "10", "2026-07-10"), Chq(2, "10", "2026-08-10") };
        var july = new ReportPeriod(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31));

        ChequeReport.Build(cheques, july).Count.Should().Be(1);
    }
}
