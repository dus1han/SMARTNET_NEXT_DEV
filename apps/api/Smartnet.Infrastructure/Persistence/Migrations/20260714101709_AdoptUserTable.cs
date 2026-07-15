using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Smartnet.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Brings the legacy <c>user_m</c> table under EF's control — additively.
    /// </summary>
    /// <remarks>
    /// HAND-WRITTEN, and it has to stay that way. `dotnet ef migrations add` generated a
    /// CreateTable for user_m — it has no way to know the table already exists — and a DropTable
    /// in Down(), which would have destroyed the live users table. If this migration is ever
    /// regenerated, it must be rewritten like this again.
    ///
    /// Everything here is additive, per DEVELOPMENT.md §8. The legacy app keeps reading and
    /// writing user_m throughout, so every new column is nullable or defaulted, and no existing
    /// column is renamed, retyped, narrowed or dropped. The plaintext `password` column stays
    /// until Phase 9.
    /// </remarks>
    public partial class AdoptUserTable : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // --- Authentication -------------------------------------------------------------
            // password_hash stays null until the user next logs in, at which point their
            // plaintext password is verified once and upgraded to a hash. Both apps keep working
            // through the cutover window because neither column is removed.
            migrationBuilder.AddColumn<string>(
                name: "password_hash",
                table: "user_m",
                type: "varchar(255)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "password_changed_at",
                table: "user_m",
                type: "datetime",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "must_change_password",
                table: "user_m",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "failed_login_count",
                table: "user_m",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "locked_until",
                table: "user_m",
                type: "datetime",
                nullable: true);

            // --- Audit columns --------------------------------------------------------------
            // created_at is NOT NULL but carries a server default, so a legacy INSERT that names
            // none of these columns still succeeds.
            migrationBuilder.AddColumn<long>(
                name: "created_by", table: "user_m", type: "bigint", nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "created_at",
                table: "user_m",
                type: "datetime",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AddColumn<long>(
                name: "updated_by", table: "user_m", type: "bigint", nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "updated_at", table: "user_m", type: "datetime", nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "deleted_by", table: "user_m", type: "bigint", nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "deleted_at", table: "user_m", type: "datetime", nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "row_version",
                table: "user_m",
                type: "int",
                nullable: false,
                defaultValue: 1);

            // --- The primary key ------------------------------------------------------------
            // `id` is already AUTO_INCREMENT, but it sits under a NON-UNIQUE index: it is not a
            // primary key, and nothing has ever stopped two rows sharing an id. (Finding 6 —
            // three primary keys in a 49-table database.) EF cannot write to a keyless entity,
            // and an audit row cannot name the row it describes without one. So the key is first.
            //
            // This is safe because AUTO_INCREMENT has in fact kept the ids unique. If this ALTER
            // fails with a duplicate-key error, STOP — it means user_m has duplicate ids, and
            // that is a data problem to settle before anything relies on `id` meaning one person.
            // The constraint name is EF's, not the database's: MySQL calls every primary key
            // "PRIMARY" and rejects that as an explicit constraint name ("Incorrect index name").
            migrationBuilder.AddPrimaryKey(
                name: "PK_user_m",
                table: "user_m",
                column: "id");

            // Login looks a user up by username on every authentication. The legacy table has no
            // index on the column at all.
            migrationBuilder.CreateIndex(
                name: "IX_user_m_username",
                table: "user_m",
                column: "username");

            // --- Every existing password is already compromised ------------------------------
            // Four characters, stored in plaintext, in a database whose credentials were
            // published in the application's source code. There is no version of this where they
            // are still secret. So everyone changes their password at their next login; the
            // hash-on-login upgrade path handles that login itself.
            migrationBuilder.Sql("UPDATE user_m SET must_change_password = 1");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Note what is absent: DropTable. Down() removes only what Up() added. The legacy
            // table, and every row in it, predates this project and outlives a rollback.
            migrationBuilder.DropIndex(name: "IX_user_m_username", table: "user_m");
            migrationBuilder.DropPrimaryKey(name: "PK_user_m", table: "user_m");

            foreach (var column in new[]
            {
                "password_hash", "password_changed_at", "must_change_password",
                "failed_login_count", "locked_until",
                "created_by", "created_at", "updated_by", "updated_at",
                "deleted_by", "deleted_at", "row_version",
            })
            {
                migrationBuilder.DropColumn(name: column, table: "user_m");
            }
        }
    }
}
