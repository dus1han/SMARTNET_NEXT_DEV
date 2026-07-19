using Microsoft.EntityFrameworkCore.Migrations;
using Smartnet.Domain.Identity;

#nullable disable

namespace Smartnet.Infrastructure.Persistence.Migrations;

/// <summary>
/// Gives every user a dashboard, so the "exactly one" rule holds for the accounts that already exist.
/// </summary>
/// <remarks>
/// The rule is enforced from the moment permissions are next saved, but nothing had applied it to the
/// users already in the system. Two of the three held the management dashboard; <c>showroom</c> held
/// neither and would have landed on a page telling them to ask an administrator — which is a poor way to
/// discover a new feature.
///
/// <para><b>The operations dashboard is the safe default</b>, and the direction of that default is the
/// point: granting the management one to somebody who should not have it discloses the margin, while
/// granting this one to somebody who should have had the other costs them a permission change. Only the
/// second mistake is reversible.</para>
///
/// <para>Written as a per-user override rather than against a role. Which dashboard somebody gets is a
/// decision about that person, and inferring it from a role they share with others would hand it to
/// everyone who happens to be a Storeman.</para>
/// </remarks>
public partial class Phase8AssignOperationsDashboard : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Anyone who effectively holds neither dashboard — by role or by override — gets the operations
        // one. Users who already have either are left exactly as they are.
        migrationBuilder.Sql($"""
            INSERT INTO user_permission_overrides (user_id, permission, granted, created_at, row_version)
            SELECT u.id, '{Permissions.DashboardOperations}', 1, UTC_TIMESTAMP(), 1
            FROM user_m u
            WHERE NOT EXISTS (
                SELECT 1 FROM user_roles ur
                JOIN role_permissions rp ON rp.role_id = ur.role_id
                WHERE ur.user_id = u.id
                  AND rp.permission IN ('{Permissions.Dashboard}', '{Permissions.DashboardOperations}')
            )
            AND NOT EXISTS (
                SELECT 1 FROM user_permission_overrides o
                WHERE o.user_id = u.id
                  AND o.permission IN ('{Permissions.Dashboard}', '{Permissions.DashboardOperations}')
                  AND o.granted = 1
                  AND o.deleted_at IS NULL
            )
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Only the grants this migration could have made. A dashboard assigned deliberately since is not
        // this migration's to take away, but there is no way to tell the two apart, so Down removes the
        // operations override wholesale and the rule is re-applied by running Up again.
        migrationBuilder.Sql($"""
            DELETE FROM user_permission_overrides
            WHERE permission = '{Permissions.DashboardOperations}' AND granted = 1 AND deleted_at IS NULL
            """);
    }
}
