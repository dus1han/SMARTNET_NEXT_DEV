using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Smartnet.Domain.Identity;
using Smartnet.Infrastructure.Identity;
using Smartnet.Infrastructure.Persistence;
using Smartnet.Tests.Auditing;

namespace Smartnet.Tests.Identity;

[Collection(nameof(AuditCollection))]
public sealed class PermissionServiceTests
{
    private readonly AuditFixture _fixture;

    public PermissionServiceTests(AuditFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task A_users_permissions_are_the_union_of_their_roles()
    {
        await using var db = _fixture.CreateContext(new FakeChangeContext());
        var service = new PermissionService(db);

        var user = await GivenUser(db, "two-hats");
        await GivenRole(db, user.Id, "Sales", [Permissions.CustomerM, Permissions.ItemInvoice]);
        await GivenRole(db, user.Id, "Store", [Permissions.ItemStock, Permissions.ItemM]);

        var effective = await service.GetEffectivePermissionsAsync(user.Id);

        effective.Should().BeEquivalentTo([
            Permissions.CustomerM, Permissions.ItemInvoice,
            Permissions.ItemStock, Permissions.ItemM,
        ]);
    }

    [Fact]
    public async Task An_override_can_add_a_permission_no_role_grants()
    {
        await using var db = _fixture.CreateContext(new FakeChangeContext());
        var service = new PermissionService(db);

        var user = await GivenUser(db, "one-extra-thing");
        await GivenRole(db, user.Id, "Counter", [Permissions.CustomerM]);

        db.UserPermissionOverrides.Add(new UserPermissionOverride
        {
            UserId = user.Id,
            Permission = Permissions.Cheques,
            Granted = true,
        });

        await db.SaveChangesAsync();

        var effective = await service.GetEffectivePermissionsAsync(user.Id);

        effective.Should().Contain(Permissions.Cheques);
    }

    [Fact]
    public async Task Role_granted_permissions_exclude_the_overrides()
    {
        await using var db = _fixture.CreateContext(new FakeChangeContext());
        var service = new PermissionService(db);

        var user = await GivenUser(db, "role-vs-override");
        await GivenRole(db, user.Id, "Sales", [Permissions.CustomerM, Permissions.ItemInvoice]);

        // An override grants one more and denies one the role gave. Role-granted must report the
        // ROLES only — it is what the permissions editor subtracts from to decide grant vs deny.
        db.UserPermissionOverrides.AddRange(
            new UserPermissionOverride { UserId = user.Id, Permission = Permissions.Cheques, Granted = true },
            new UserPermissionOverride { UserId = user.Id, Permission = Permissions.ItemInvoice, Granted = false });

        await db.SaveChangesAsync();

        var roleGranted = await service.GetRoleGrantedPermissionsAsync(user.Id);

        roleGranted.Should().BeEquivalentTo([Permissions.CustomerM, Permissions.ItemInvoice]);
        roleGranted.Should().NotContain(Permissions.Cheques, "that comes from an override, not a role");

        // And effective still reflects the overrides on top — the two methods answer different
        // questions, and the editor needs both.
        var effective = await service.GetEffectivePermissionsAsync(user.Id);
        effective.Should().Contain(Permissions.Cheques);
        effective.Should().NotContain(Permissions.ItemInvoice);
    }

    [Fact]
    public async Task A_revoking_override_beats_a_role_that_grants_it()
    {
        await using var db = _fixture.CreateContext(new FakeChangeContext());
        var service = new PermissionService(db);

        var user = await GivenUser(db, "all-but-one");
        await GivenRole(db, user.Id, "Accounts", [Permissions.Payments, Permissions.Expenses]);

        db.UserPermissionOverrides.Add(new UserPermissionOverride
        {
            UserId = user.Id,
            Permission = Permissions.Expenses,
            Granted = false,
        });

        await db.SaveChangesAsync();

        var effective = await service.GetEffectivePermissionsAsync(user.Id);

        // The narrower, deliberate statement about this one person beats the broader one about
        // their job. Otherwise a revocation is just a suggestion.
        effective.Should().Contain(Permissions.Payments);
        effective.Should().NotContain(Permissions.Expenses);
    }

    [Fact]
    public async Task Revoking_a_role_immediately_stops_it_granting_anything()
    {
        await using var db = _fixture.CreateContext(new FakeChangeContext());
        var service = new PermissionService(db);

        var user = await GivenUser(db, "departing");
        var role = await GivenRole(db, user.Id, "Temp", [Permissions.Payments]);

        var assignment = await db.UserRoles
            .SingleAsync(a => a.UserId == user.Id && a.RoleId == role.Id);

        db.UserRoles.Remove(assignment); // soft delete, via the interceptor
        await db.SaveChangesAsync();

        var effective = await service.GetEffectivePermissionsAsync(user.Id);

        // A soft-deleted assignment that kept granting access would be the worst of both worlds:
        // the audit log says the access was removed, and the user still has it.
        effective.Should().BeEmpty();
    }

    [Fact]
    public async Task The_legacy_table_is_rewritten_including_the_permissions_being_taken_away()
    {
        await using var db = _fixture.CreateContext(new FakeChangeContext());
        var service = new PermissionService(db);

        var user = await GivenUser(db, "sync-target");
        var role = await GivenRole(db, user.Id, "Broad", [Permissions.CustomerM, Permissions.Cheques]);

        await service.SyncToLegacyAsync(user.Id);

        (await LegacyFlag(user.Id, Permissions.CustomerM)).Should().Be("1");
        (await LegacyFlag(user.Id, Permissions.Cheques)).Should().Be("1");
        (await LegacyFlag(user.Id, Permissions.Payments)).Should().Be("0");

        // Now take Cheques away.
        var granted = await db.RolePermissions
            .SingleAsync(p => p.RoleId == role.Id && p.Permission == Permissions.Cheques);

        db.RolePermissions.Remove(granted);
        await db.SaveChangesAsync();

        await service.SyncToLegacyAsync(user.Id);

        // The bug this exists to not have: writing only the grants would leave cheques sitting at
        // "1" in the table the LEGACY app reads, so the user would keep the access in the old app
        // after losing it in the new one.
        (await LegacyFlag(user.Id, Permissions.Cheques)).Should().Be("0");
        (await LegacyFlag(user.Id, Permissions.CustomerM)).Should().Be("1");
    }

    [Fact]
    public async Task Permissions_that_only_exist_in_the_new_app_are_not_written_to_the_legacy_table()
    {
        await using var db = _fixture.CreateContext(new FakeChangeContext());
        var service = new PermissionService(db);

        var user = await GivenUser(db, "new-perms");
        await GivenRole(db, user.Id, "Admin-ish", [Permissions.SettingsManage, Permissions.CustomerM]);

        // The legacy table has no column for settings.manage, and inventing one would mean
        // altering a table the old app parses by name.
        await service.SyncToLegacyAsync(user.Id);

        var effective = await service.GetEffectivePermissionsAsync(user.Id);
        effective.Should().Contain(Permissions.SettingsManage);

        (await LegacyFlag(user.Id, Permissions.CustomerM)).Should().Be("1");
    }

    // --- plumbing ---------------------------------------------------------------------------

    private static async Task<User> GivenUser(TestDbContext db, string username)
    {
        var user = new User { Username = username, Name = username, Ustat = "Active", Addedby = "test" };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private static async Task<Role> GivenRole(
        TestDbContext db,
        long userId,
        string name,
        string[] permissions)
    {
        var role = new Role
        {
            // Unique per test: the roles table has a unique index on (company_id, name).
            Name = $"{name}-{userId}",
            Permissions = [.. permissions.Select(p => new RolePermission { Permission = p })],
        };

        db.Roles.Add(role);
        await db.SaveChangesAsync();

        db.UserRoles.Add(new UserRole { UserId = userId, RoleId = role.Id });
        await db.SaveChangesAsync();

        return role;
    }

    private async Task<string> LegacyFlag(long userId, string permission)
    {
        await using var db = _fixture.CreateContext(new FakeChangeContext());

        var key = userId.ToString(System.Globalization.CultureInfo.InvariantCulture);

        var row = await db.Set<Dictionary<string, object>>(LegacyUserPermissions.EntityName)
            .SingleAsync(r => (string)r[LegacyUserPermissions.UserIdColumn] == key);

        return (string)row[permission];
    }
}
