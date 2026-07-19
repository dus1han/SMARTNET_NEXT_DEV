using System.Security.Claims;
using FluentAssertions;
using Smartnet.Api.Auditing;
using Smartnet.Api.Auth;
using Smartnet.Domain.Identity;

namespace Smartnet.Tests.Auth;

/// <summary>
/// Who may see cost and profit. The rule is one line; what it guards is every margin figure in the
/// system, so it is pinned rather than assumed.
/// </summary>
public sealed class MarginAccessTests
{
    private static ClaimsPrincipal With(params string[] permissions) =>
        new(new ClaimsIdentity(permissions.Select(p => new Claim(SmartnetClaims.Permission, p)), "test"));

    [Fact]
    public void The_management_dashboard_carries_margin_access() =>
        MarginAccess.CanSee(With(Permissions.Dashboard)).Should().BeTrue();

    [Fact]
    public void The_operations_dashboard_does_not()
    {
        // The whole point of that dashboard is what it withholds; margin following it elsewhere is the
        // difference between a policy and a screen that merely looks tidy.
        MarginAccess.CanSee(With(Permissions.DashboardOperations)).Should().BeFalse();
    }

    [Fact]
    public void A_dev_admin_sees_margin_whatever_else_they_hold() =>
        MarginAccess.CanSee(With(Permissions.SystemDevAdmin)).Should().BeTrue();

    [Fact]
    public void Holding_every_report_permission_is_not_margin_access()
    {
        // The reports are gated separately: somebody may run the sales report and still not be entitled
        // to its cost column. This is the case that would break if the check ever moved onto a report
        // permission instead.
        var reporter = With(
            Permissions.SalesReport,
            Permissions.CustomerSalesReport,
            Permissions.JobCardsReport,
            Permissions.DashboardOperations);

        MarginAccess.CanSee(reporter).Should().BeFalse();
    }

    [Fact]
    public void A_caller_with_no_permissions_at_all_sees_nothing() =>
        MarginAccess.CanSee(With()).Should().BeFalse();

    [Fact]
    public void Redaction_zeroes_a_figure_for_a_caller_who_may_not_see_it()
    {
        var clerk = With(Permissions.DashboardOperations);
        var owner = With(Permissions.Dashboard);

        clerk.Redact(1234.56m).Should().Be(0m);
        owner.Redact(1234.56m).Should().Be(1234.56m);
    }

    [Fact]
    public void Redaction_nulls_an_optional_figure_rather_than_zeroing_it()
    {
        // A nullable cost reads as "not recorded" at zero and "not shown to you" at null, and an item
        // that genuinely cost nothing is a different statement from one whose cost is withheld.
        var clerk = With(Permissions.DashboardOperations);

        clerk.Redact((decimal?)99m).Should().BeNull();
        With(Permissions.Dashboard).Redact((decimal?)99m).Should().Be(99m);
    }

    [Fact]
    public void The_two_dashboards_are_the_only_dashboards()
    {
        // If a third is ever added, this fails and whoever adds it has to decide which side of the
        // margin line it sits on rather than discovering the answer in production.
        Permissions.DashboardPermissions.Should().BeEquivalentTo(
            [Permissions.Dashboard, Permissions.DashboardOperations]);
    }
}
