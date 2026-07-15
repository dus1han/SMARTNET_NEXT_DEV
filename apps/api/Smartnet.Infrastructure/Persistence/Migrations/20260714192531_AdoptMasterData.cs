using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Smartnet.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Brings <c>cus_m</c>, <c>sup_m</c>, <c>item_m</c> and <c>item_stock</c> under EF's control.
    /// </summary>
    /// <remarks>
    /// <b>HAND-WRITTEN</b>, like <c>AdoptUserTable</c>, and for the same reason. <c>dotnet ef
    /// migrations add</c> generated a <c>CreateTable</c> for each of these — it has no way to know
    /// they already exist — and a <c>DropTable</c> for each in <c>Down()</c>, which would have
    /// destroyed 223 customers, 86 suppliers and 500 items. All of that was replaced by what is
    /// below. <b>If this migration is ever regenerated, it must be rewritten like this again.</b>
    ///
    /// <para><b>Additive, per DEVELOPMENT.md §8.</b> The legacy app is still the live app and still
    /// writes all four tables, so every column added is nullable or defaulted, and nothing is
    /// renamed, narrowed or dropped. New columns land at the <i>end</i> of each table, where a
    /// positional <c>INSERT … VALUES (…)</c> with no column list cannot reach them.</para>
    ///
    /// <para><b>The retypes are not additive — they are the point.</b> Money and dates are
    /// <c>varchar(100)</c> in this schema (Finding 5): <c>climit</c>, <c>unitcost</c>,
    /// <c>quantity</c>, <c>balance</c>, <c>indate</c>. A varchar cannot stop <c>"12,500"</c> or
    /// <c>"abc"</c> reaching a credit limit, and cannot be summed without a cast. The retype is
    /// <b>verified safe</b>: every value in all four tables parses, and there are no blanks. MariaDB
    /// still accepts the legacy app's numeric strings — <c>INSERT … VALUES ('25000')</c> into a
    /// DECIMAL column works — so the old app keeps writing. What the column now rejects is junk.</para>
    ///
    /// <para><c>profit_percent</c> needs nothing: it is one of the three tables in this schema that
    /// already has a primary key. It is mapped, not migrated.</para>
    /// </remarks>
    public partial class AdoptMasterData : Migration
    {
        /// <summary>The three master tables with no key of any kind.</summary>
        private static readonly string[] Keyless = ["cus_m", "sup_m", "item_m"];

        private static readonly string[] AllFour = ["cus_m", "sup_m", "item_m", "item_stock"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var table in AllFour)
            {
                // created_at is NOT NULL but carries a server default, so a legacy INSERT naming none
                // of these columns still succeeds. row_version starts at 1 for the same reason: a row
                // the old app inserts has to be a valid row for the concurrency check.
                migrationBuilder.Sql($"""
                    ALTER TABLE `{table}`
                        ADD COLUMN `created_by`  bigint   NULL,
                        ADD COLUMN `created_at`  datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
                        ADD COLUMN `updated_by`  bigint   NULL,
                        ADD COLUMN `updated_at`  datetime NULL,
                        ADD COLUMN `deleted_by`  bigint   NULL,
                        ADD COLUMN `deleted_at`  datetime NULL,
                        ADD COLUMN `row_version` int      NOT NULL DEFAULT 1
                    """);
            }

            foreach (var table in Keyless)
            {
                // These three have NO key: no id column, no unique index, nothing. A customer is
                // identified by a hand-typed varchar, and nothing has ever stopped two rows being
                // byte-for-byte identical, or an UPDATE hitting more rows than intended (Finding 6).
                //
                // EF cannot write to a keyless entity, and an audit row cannot name the row it
                // describes without one. So the key comes first, and everything else follows it.
                migrationBuilder.Sql($"""
                    ALTER TABLE `{table}`
                        ADD COLUMN `id` bigint NOT NULL AUTO_INCREMENT,
                        ADD PRIMARY KEY (`id`)
                    """);
            }

            // item_stock is the same defect in disguise: it HAS an AUTO_INCREMENT `id`, but under a
            // plain non-unique KEY rather than a primary key. Promote it; drop the index it leaves.
            migrationBuilder.Sql("ALTER TABLE `item_stock` ADD PRIMARY KEY (`id`)");
            migrationBuilder.Sql("ALTER TABLE `item_stock` DROP INDEX `id`");

            // --- The retypes ----------------------------------------------------------------

            // The credit limit: NOT NULL already, no blanks, all 223 values numeric. 198 of them are
            // zero — worth knowing before Phase 5 builds a credit-limit wall that applies to 25
            // customers.
            migrationBuilder.Sql(
                "ALTER TABLE `cus_m` MODIFY COLUMN `climit` decimal(18,4) NOT NULL DEFAULT 0");

            // c_form (the trading entity a customer is associated with) and pro (their margin band):
            // varchars holding another table's primary key, with nothing enforcing it.
            //
            // Retyped, but NOT made foreign keys. 42 customers have no c_form at all, and a FK on a
            // column the legacy app writes as a string is a constraint waiting to reject a legacy
            // INSERT. The FKs belong with the legacy app's retirement in Phase 9.
            migrationBuilder.Sql("ALTER TABLE `cus_m` MODIFY COLUMN `c_form` bigint NULL");
            migrationBuilder.Sql("ALTER TABLE `cus_m` MODIFY COLUMN `pro` bigint NULL");

            // Stock: cost, quantity and balance are money held as text; indate and enteredat are
            // dates held as text. Six rows, all clean.
            migrationBuilder.Sql("""
                ALTER TABLE `item_stock`
                    MODIFY COLUMN `unitcost`  decimal(18,4) NULL,
                    MODIFY COLUMN `quantity`  decimal(18,4) NULL,
                    MODIFY COLUMN `balance`   decimal(18,4) NULL,
                    MODIFY COLUMN `indate`    date          NULL,
                    MODIFY COLUMN `enteredat` datetime      NULL
                """);

            // --- The indexes ----------------------------------------------------------------

            // The codes ARE the business's identifiers — "C-1", "I-153" — and nothing has ever
            // stopped two customers sharing one. Checked: zero duplicates in all three tables. That
            // is luck, not protection. This is the protection.
            //
            // If one of these fails with a duplicate-key error, STOP. It means duplicates have been
            // created since this was checked, and which row is the real customer is a question for
            // the business, not for a migration.
            migrationBuilder.Sql("CREATE UNIQUE INDEX `IX_cus_m_cuscode` ON `cus_m` (`cuscode`)");
            migrationBuilder.Sql("CREATE UNIQUE INDEX `IX_sup_m_supcode` ON `sup_m` (`supcode`)");
            migrationBuilder.Sql("CREATE UNIQUE INDEX `IX_item_m_itemcode` ON `item_m` (`itemcode`)");

            // Stock is read one item at a time: "what have I got, and in which batches?"
            migrationBuilder.Sql("CREATE INDEX `IX_item_stock_item_code` ON `item_stock` (`item_code`)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Note what is absent: DropTable. Down() removes only what Up() added. These tables, and
            // every row in them, predate this project and outlive a rollback.
            migrationBuilder.Sql("DROP INDEX `IX_cus_m_cuscode` ON `cus_m`");
            migrationBuilder.Sql("DROP INDEX `IX_sup_m_supcode` ON `sup_m`");
            migrationBuilder.Sql("DROP INDEX `IX_item_m_itemcode` ON `item_m`");
            migrationBuilder.Sql("DROP INDEX `IX_item_stock_item_code` ON `item_stock`");

            // Back to varchar. Lossless in this direction — a DECIMAL renders as a string — and it
            // does mean the junk becomes possible again, which is what rolling this back means.
            migrationBuilder.Sql("ALTER TABLE `cus_m` MODIFY COLUMN `climit` varchar(100) NOT NULL");
            migrationBuilder.Sql("ALTER TABLE `cus_m` MODIFY COLUMN `c_form` varchar(100) NULL");
            migrationBuilder.Sql("ALTER TABLE `cus_m` MODIFY COLUMN `pro` varchar(100) NULL");

            migrationBuilder.Sql("""
                ALTER TABLE `item_stock`
                    MODIFY COLUMN `unitcost`  varchar(100) NULL,
                    MODIFY COLUMN `quantity`  varchar(100) NULL,
                    MODIFY COLUMN `balance`   varchar(100) NULL,
                    MODIFY COLUMN `indate`    varchar(100) NULL,
                    MODIFY COLUMN `enteredat` varchar(100) NULL
                """);

            // item_stock keeps its id — it had one before this migration — and gets its non-unique
            // index back in place of the primary key.
            migrationBuilder.Sql("ALTER TABLE `item_stock` DROP PRIMARY KEY");
            migrationBuilder.Sql("CREATE INDEX `id` ON `item_stock` (`id`)");

            foreach (var table in Keyless)
            {
                // AUTO_INCREMENT has to go before the key it depends on.
                migrationBuilder.Sql($"ALTER TABLE `{table}` MODIFY COLUMN `id` bigint NOT NULL");
                migrationBuilder.Sql($"ALTER TABLE `{table}` DROP PRIMARY KEY");
                migrationBuilder.Sql($"ALTER TABLE `{table}` DROP COLUMN `id`");
            }

            foreach (var table in AllFour)
            {
                migrationBuilder.Sql($"""
                    ALTER TABLE `{table}`
                        DROP COLUMN `created_by`,
                        DROP COLUMN `created_at`,
                        DROP COLUMN `updated_by`,
                        DROP COLUMN `updated_at`,
                        DROP COLUMN `deleted_by`,
                        DROP COLUMN `deleted_at`,
                        DROP COLUMN `row_version`
                    """);
            }
        }
    }
}
