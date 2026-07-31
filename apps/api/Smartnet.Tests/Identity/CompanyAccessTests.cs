using FluentAssertions;
using Smartnet.Domain.Identity;
using Smartnet.Domain.Settings;
using Smartnet.Infrastructure.Identity;
using Smartnet.Infrastructure.Persistence;
using Smartnet.Tests.Auditing;

namespace Smartnet.Tests.Identity;

/// <summary>
/// Which companies a user may act in — and, far more often, the reason a screen is empty.
/// </summary>
/// <remarks>
/// <para>Company scope is deliberately <b>not</b> an authorisation boundary (see the remarks on
/// <see cref="ICompanyAccessService"/>): the two companies are trading entities the same staff work
/// across, and the switcher chooses which one you are issuing under. Permissions decide what someone
/// may do. But every list, report and lookup filters on the accessible set, so an empty set is
/// indistinguishable from an empty database — the user sees their menus, because the permissions are
/// real and in their token, and nothing behind any of them.</para>
///
/// <para>That is exactly what happened. Access is granted permission by permission on the users
/// screen, which writes overrides and no role assignment at all, so a user set up that way had no
/// role row — and "no role row" was read as "no companies" rather than as "nothing said about
/// scope".</para>
/// </remarks>
[Collection(nameof(AuditCollection))]
public sealed class CompanyAccessTests
{
    private readonly AuditFixture _fixture;

    /// <summary>Outside the range the rest of the collection uses; see UserAdministrationTests.</summary>
    private static long _nextUserId = 950_000;

    public CompanyAccessTests(AuditFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task A_user_with_no_role_assignment_can_act_in_every_company()
    {
        await using var db = _fixture.CreateContext(new FakeChangeContext());

        var company = await GivenCompany(db, "Access — no roles");
        var user = await GivenUser(db, "cat-no-roles");

        // No role assignment at all: the state of every user whose access was granted permission by
        // permission. This used to come back empty, and every screen they opened was blank.
        var accessible = await new CompanyAccessService(db, new PermissionService(db))
            .GetAccessibleCompanyIdsAsync(user.Id);

        accessible.Should().NotBeEmpty("an empty set makes every screen look like an empty database");
        accessible.Should().Contain(company.Id);
    }

    [Fact]
    public async Task A_globally_assigned_role_still_grants_every_company()
    {
        await using var db = _fixture.CreateContext(new FakeChangeContext());

        var company = await GivenCompany(db, "Access — global role");
        var user = await GivenUser(db, "cat-global-role");
        await GivenRole(db, user.Id, companyId: null);

        var accessible = await new CompanyAccessService(db, new PermissionService(db))
            .GetAccessibleCompanyIdsAsync(user.Id);

        accessible.Should().Contain(company.Id);
    }

    [Fact]
    public async Task An_assignment_naming_a_company_still_narrows_to_it()
    {
        await using var db = _fixture.CreateContext(new FakeChangeContext());

        var mine = await GivenCompany(db, "Access — scoped to this one");
        var theirs = await GivenCompany(db, "Access — not this one");
        var user = await GivenUser(db, "cat-scoped");
        await GivenRole(db, user.Id, companyId: mine.Id);

        var accessible = await new CompanyAccessService(db, new PermissionService(db))
            .GetAccessibleCompanyIdsAsync(user.Id);

        // The dormant mechanism still works. Opening up "no assignment" must not open up an
        // assignment that deliberately names one company — that is the case it exists for.
        accessible.Should().Contain(mine.Id);
        accessible.Should().NotContain(theirs.Id);
    }

    [Fact]
    public async Task A_developer_gets_every_company_without_any_assignment()
    {
        await using var db = _fixture.CreateContext(new FakeChangeContext());

        var company = await GivenCompany(db, "Access — dev admin");
        var user = await GivenUser(db, "cat-dev-admin");

        db.UserPermissionOverrides.Add(new UserPermissionOverride
        {
            UserId = user.Id,
            Permission = Permissions.SystemDevAdmin,
            Granted = true,
        });
        await db.SaveChangesAsync();

        var accessible = await new CompanyAccessService(db, new PermissionService(db))
            .GetAccessibleCompanyIdsAsync(user.Id);

        accessible.Should().Contain(company.Id);
    }

    // --- Seeding ----------------------------------------------------------------------------------

    private static async Task<Company> GivenCompany(TestDbContext db, string name)
    {
        var company = new Company { Name = $"{name} {Interlocked.Read(ref _nextUserId)}", VatCode = "1" };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        return company;
    }

    private static async Task<User> GivenUser(TestDbContext db, string username)
    {
        var user = new User
        {
            Id = Interlocked.Increment(ref _nextUserId),
            Username = username,
            Name = username,
            Ustat = "Active",
            PasswordHash = "not-a-real-hash",
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private static async Task GivenRole(TestDbContext db, long userId, long? companyId)
    {
        var role = new Role { Name = $"Access role {userId}", IsSystem = false };
        db.Roles.Add(role);
        await db.SaveChangesAsync();

        db.UserRoles.Add(new UserRole { UserId = userId, RoleId = role.Id, CompanyId = companyId });
        await db.SaveChangesAsync();
    }
}
