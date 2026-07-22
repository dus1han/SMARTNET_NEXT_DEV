using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore.Metadata;
using Smartnet.Domain.Identity;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Smartnet.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RolesAndPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "roles",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: true),
                    name = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    description = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    is_system = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    deleted_by = table.Column<long>(type: "bigint", nullable: true),
                    deleted_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    row_version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_roles", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "user_permission_overrides",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    permission = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    granted = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    deleted_by = table.Column<long>(type: "bigint", nullable: true),
                    deleted_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    row_version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_permission_overrides", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            // --- user_permissions: ADOPTED, not created -----------------------------------
            // EF generated a CreateTable here, and a DropTable in Down(). Both were wrong and
            // both were replaced by hand: this table already exists, holds the live permissions,
            // and is still being read by the legacy app. It gains a primary key (it had none —
            // Finding 6) and nothing else. See AdoptUserTable for the same surgery on user_m.
            migrationBuilder.AddPrimaryKey(
                name: "PK_user_permissions",
                table: "user_permissions",
                column: "user_id");

            migrationBuilder.CreateTable(
                name: "user_roles",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    role_id = table.Column<long>(type: "bigint", nullable: false),
                    company_id = table.Column<long>(type: "bigint", nullable: true),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    deleted_by = table.Column<long>(type: "bigint", nullable: true),
                    deleted_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    row_version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_roles", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "role_permissions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    role_id = table.Column<long>(type: "bigint", nullable: false),
                    permission = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_role_permissions", x => x.id);
                    table.ForeignKey(
                        name: "FK_role_permissions_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_role_permissions_role_id_permission",
                table: "role_permissions",
                columns: new[] { "role_id", "permission" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_roles_company_id_name",
                table: "roles",
                columns: new[] { "company_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_permission_overrides_user_id_permission",
                table: "user_permission_overrides",
                columns: new[] { "user_id", "permission" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_roles_user_id_role_id_company_id",
                table: "user_roles",
                columns: new[] { "user_id", "role_id", "company_id" },
                unique: true);

            Seed(migrationBuilder);
        }

        /// <summary>
        /// Seeds the system roles, and moves every existing user onto a role that grants exactly
        /// what they already had.
        /// </summary>
        /// <remarks>
        /// <b>Nobody's access changes on the day this runs.</b> That is the single most important
        /// property of this migration: a permissions change that silently widens or narrows what
        /// somebody can do, on a Tuesday, during a migration, is how you lose trust in the whole
        /// project. Each user gets a generated role holding their current flags, verbatim, and an
        /// administrator can tidy them into real roles afterwards, deliberately.
        ///
        /// <para>The permission lists are generated from <see cref="Permissions"/> rather than
        /// typed out, so the seed cannot drift from the catalogue the policies are built from.</para>
        /// </remarks>
        private static void Seed(MigrationBuilder migrationBuilder)
        {
            // --- The two system roles ------------------------------------------------------
            migrationBuilder.Sql($"""
                INSERT INTO roles (company_id, name, description, is_system, created_at, row_version)
                VALUES
                  (NULL, '{Role.DevAdmin}',
                   'Developer/superuser. Crosses every company and reaches the dev-only surfaces.', 1, UTC_TIMESTAMP(), 1),
                  (NULL, '{Role.CompanyAdmin}',
                   'Full business permissions within one company.', 1, UTC_TIMESTAMP(), 1)
                """);

            // Dev_Admin gets everything grantable, including system.dev_admin — but NOT the operations
            // dashboard. Permissions.All holds both dashboards and is not a valid grant to anybody; see
            // Permissions.AdministratorGrant.
            GrantAll(migrationBuilder, Role.DevAdmin, Permissions.AdministratorGrant);

            // Company_Admin gets everything EXCEPT system.dev_admin — that is the entire
            // difference between the two, and the reason they are separate roles. A company
            // administrator runs their company; they do not get to see other companies' books.
            GrantAll(
                migrationBuilder,
                Role.CompanyAdmin,
                [.. Permissions.AdministratorGrant.Where(p => p != Permissions.SystemDevAdmin)]);

            // --- One role per existing user, preserving their exact flags -------------------
            migrationBuilder.Sql("""
                INSERT INTO roles (company_id, name, description, is_system, created_at, row_version)
                SELECT NULL,
                       CONCAT('Legacy: ', LEFT(u.username, 50)),
                       'Generated at migration from this user''s original permission flags.',
                       0, UTC_TIMESTAMP(), 1
                FROM user_m u
                WHERE u.username IS NOT NULL
                """);

            // Copy each flag across. The legacy table stores '1' / '0' as varchar.
            foreach (var permission in Permissions.LegacyPermissions)
            {
                migrationBuilder.Sql($"""
                    INSERT INTO role_permissions (role_id, permission)
                    SELECT r.id, '{permission}'
                    FROM user_m u
                    JOIN roles r ON r.name = CONCAT('Legacy: ', LEFT(u.username, 50))
                    JOIN user_permissions up ON up.user_id = u.id
                    WHERE up.`{permission}` = '1'
                    """);
            }

            // The new app has surfaces the legacy app never had — roles, settings, the audit log.
            // Nobody holds a flag for them, so nobody could administer the new system at all.
            // Whoever could already manage users is the closest thing to an administrator the old
            // app had, so they inherit the new administrative permissions too.
            foreach (var permission in new[]
            {
                Permissions.RolesManage, Permissions.SettingsManage, Permissions.AuditView,
            })
            {
                migrationBuilder.Sql($"""
                    INSERT INTO role_permissions (role_id, permission)
                    SELECT r.id, '{permission}'
                    FROM user_m u
                    JOIN roles r ON r.name = CONCAT('Legacy: ', LEFT(u.username, 50))
                    JOIN user_permissions up ON up.user_id = u.id
                    WHERE up.`{Permissions.Users}` = '1'
                    """);
            }

            // Note that system.dev_admin is granted to NOBODY here. It is the one permission that
            // must be handed over by a person, on purpose, and the handover is audited.

            migrationBuilder.Sql("""
                INSERT INTO user_roles (user_id, role_id, company_id, created_at, row_version)
                SELECT u.id, r.id, NULL, UTC_TIMESTAMP(), 1
                FROM user_m u
                JOIN roles r ON r.name = CONCAT('Legacy: ', LEFT(u.username, 50))
                """);
        }

        private static void GrantAll(
            MigrationBuilder migrationBuilder,
            string roleName,
            IReadOnlyList<string> permissions)
        {
            var values = string.Join(
                ",\n  ",
                permissions.Select(p => $"((SELECT id FROM (SELECT id FROM roles WHERE name = '{roleName}') AS r), '{p}')"));

            migrationBuilder.Sql($"""
                INSERT INTO role_permissions (role_id, permission)
                VALUES
                  {values}
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "role_permissions");

            migrationBuilder.DropTable(
                name: "user_permission_overrides");

            // NOT DropTable. user_permissions is a legacy table holding live permissions, and the
            // legacy app is still reading it. Down() gives back only the key that Up() added.
            migrationBuilder.DropPrimaryKey(
                name: "PK_user_permissions",
                table: "user_permissions");

            migrationBuilder.DropTable(
                name: "user_roles");

            migrationBuilder.DropTable(
                name: "roles");
        }
    }
}
