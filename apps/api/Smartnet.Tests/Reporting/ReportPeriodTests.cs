using FluentAssertions;
using Smartnet.Domain.Reporting;

namespace Smartnet.Tests.Reporting;

public sealed class ReportPeriodTests
{
    [Fact]
    public void An_unbounded_period_contains_every_date_including_blank()
    {
        ReportPeriod.All.ContainsIso("2026-07-15").Should().BeTrue();
        ReportPeriod.All.ContainsIso("").Should().BeTrue();
        ReportPeriod.All.ContainsIso(null).Should().BeTrue();
    }

    [Theory]
    [InlineData("2026-07-01", true)]  // the from bound is inclusive
    [InlineData("2026-07-31", true)]  // the to bound is inclusive
    [InlineData("2026-07-15", true)]
    [InlineData("2026-06-30", false)] // the day before
    [InlineData("2026-08-01", false)] // the day after
    public void A_bounded_period_includes_its_endpoints(string date, bool expected)
    {
        var july = new ReportPeriod(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31));

        july.ContainsIso(date).Should().Be(expected);
    }

    [Fact]
    public void A_bounded_period_excludes_a_blank_or_malformed_date()
    {
        // Faithful to the legacy string comparison: a blank indate sorts below any real ISO date, so a
        // bounded window leaves it out — exactly as `indate >= 'from'` did.
        var july = new ReportPeriod(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31));

        july.ContainsIso("").Should().BeFalse();
        july.ContainsIso(null).Should().BeFalse();
    }

    [Fact]
    public void An_open_ended_period_bounds_only_the_side_that_is_set()
    {
        var fromOnly = new ReportPeriod(new DateOnly(2026, 7, 1), null);
        fromOnly.ContainsIso("2030-01-01").Should().BeTrue();
        fromOnly.ContainsIso("2026-06-30").Should().BeFalse();

        var toOnly = new ReportPeriod(null, new DateOnly(2026, 7, 31));
        toOnly.ContainsIso("2000-01-01").Should().BeTrue();
        toOnly.ContainsIso("2026-08-01").Should().BeFalse();
    }
}
