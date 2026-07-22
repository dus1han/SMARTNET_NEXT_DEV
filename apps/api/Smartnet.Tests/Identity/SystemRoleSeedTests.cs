using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Smartnet.Domain.Identity;
using Smartnet.Tests.Auditing;

namespace Smartnet.Tests.Identity;

/// <summary>
/// What the two administrator roles are seeded with.
/// </summary>
/// <remarks>
/// Asserted against a database the migrations actually built, because the fault this protects against was
/// invisible in the code that caused it: the seed said <c>Permissions.All</c>, which reads like exactly
/// the right thing to give an administrator and is not — the catalogue contains both dashboards, and a
/// role handing out the pair puts an administrator on the wrong side of a radio button.
/// </remarks>
[Collection(nameof(AuditCollection))]
public sealed class SystemRoleSeedTests
{
    private readonly AuditFixture _fixture;

    public SystemRoleSeedTests(AuditFixture fixture) => _fixture = fixture;

    [Theory]
    [InlineData(Role.DevAdmin)]
    [InlineData(Role.CompanyAdmin)]
    public async Task An_administrator_role_grants_the_management_dashboard_and_not_the_other_one(string roleName)
    {
        var granted = await DashboardGrantsOf(roleName);

        granted.Should().ContainSingle("an administrator gets one dashboard, like everybody else")
            .Which.Should().Be(Permissions.Dashboard, "and it is the one with the figures on it");
    }

    [Fact]
    public async Task Dev_admin_still_gets_everything_else()
    {
        // The narrowing must be exactly one permission wide. A seed that quietly stopped granting the
        // audit log or the ledger would also satisfy the test above.
        await using var db = _fixture.CreateContext(new FakeChangeContext());

        var granted = await db.Roles
            .Where(r => r.Name == Role.DevAdmin && r.DeletedAt == null)
            .SelectMany(r => r.Permissions.Select(p => p.Permission))
            .ToListAsync();

        granted.Should().BeEquivalentTo(Permissions.AdministratorGrant);
        granted.Should().Contain(Permissions.SystemDevAdmin, "it is the role that defines the superuser");
    }

    [Fact]
    public async Task Company_admin_gets_the_same_minus_the_superuser_bit() =>
        (await GrantsOf(Role.CompanyAdmin)).Should().BeEquivalentTo(
            Permissions.AdministratorGrant.Where(p => p != Permissions.SystemDevAdmin),
            "that difference is the entire reason the two roles exist separately");

    private async Task<List<string>> GrantsOf(string roleName)
    {
        await using var db = _fixture.CreateContext(new FakeChangeContext());

        return await db.Roles
            .Where(r => r.Name == roleName && r.DeletedAt == null)
            .SelectMany(r => r.Permissions.Select(p => p.Permission))
            .ToListAsync();
    }

    private async Task<List<string>> DashboardGrantsOf(string roleName) =>
        [.. (await GrantsOf(roleName)).Where(Permissions.DashboardPermissions.Contains)];
}
