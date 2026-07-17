using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Smartnet.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase7AdoptCheques : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Adopt the legacy `cheques` table additively (Phase 7, slice 2). Like supplier_invoice it already
            // has an `id` under a non-unique KEY (Finding 6), so promote it to a real primary key rather than
            // adding one. company_id and the legacy varchars already exist (multi-company migration); the new
            // typed columns (amount, dates, supplier_id) sit beside them.

            // --- promote the existing int id to a bigint primary key ---------------------------------
            migrationBuilder.Sql("ALTER TABLE `cheques` MODIFY COLUMN `id` bigint NOT NULL AUTO_INCREMENT");
            migrationBuilder.Sql("ALTER TABLE `cheques` ADD PRIMARY KEY (`id`)");
            migrationBuilder.Sql("ALTER TABLE `cheques` DROP INDEX `id`");

            // --- the new columns -----------------------------------------------------------------------
            migrationBuilder.Sql("""
                ALTER TABLE `cheques`
                    ADD COLUMN `supplier_id`   bigint        NULL,
                    ADD COLUMN `cheque_amount` decimal(18,4) NOT NULL DEFAULT 0,
                    ADD COLUMN `cheque_date`   date          NULL,
                    ADD COLUMN `due_date`      date          NULL,
                    ADD COLUMN `printed_at`    datetime(6)   NULL,
                    ADD COLUMN `data_origin`   varchar(16)   NOT NULL DEFAULT 'legacy',
                    ADD COLUMN `created_by`    bigint        NULL,
                    ADD COLUMN `created_at`    datetime(6)   NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                    ADD COLUMN `updated_by`    bigint        NULL,
                    ADD COLUMN `updated_at`    datetime(6)   NULL,
                    ADD COLUMN `deleted_by`    bigint        NULL,
                    ADD COLUMN `deleted_at`    datetime(6)   NULL,
                    ADD COLUMN `row_version`   int           NOT NULL DEFAULT 1
                """);

            // --- the optional supplier link (a Supplier-entry cheque) ----------------------------------
            migrationBuilder.Sql("CREATE INDEX `IX_cheques_supplier_id` ON `cheques` (`supplier_id`)");
            migrationBuilder.Sql(
                "ALTER TABLE `cheques` ADD CONSTRAINT `FK_cheques_sup_m_supplier_id` " +
                "FOREIGN KEY (`supplier_id`) REFERENCES `sup_m` (`id`) ON DELETE RESTRICT");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE `cheques` DROP FOREIGN KEY `FK_cheques_sup_m_supplier_id`");
            migrationBuilder.Sql("DROP INDEX `IX_cheques_supplier_id` ON `cheques`");

            migrationBuilder.Sql("""
                ALTER TABLE `cheques`
                    DROP COLUMN `supplier_id`,
                    DROP COLUMN `cheque_amount`,
                    DROP COLUMN `cheque_date`,
                    DROP COLUMN `due_date`,
                    DROP COLUMN `printed_at`,
                    DROP COLUMN `data_origin`,
                    DROP COLUMN `created_by`,
                    DROP COLUMN `created_at`,
                    DROP COLUMN `updated_by`,
                    DROP COLUMN `updated_at`,
                    DROP COLUMN `deleted_by`,
                    DROP COLUMN `deleted_at`,
                    DROP COLUMN `row_version`
                """);

            // AUTO_INCREMENT has to go before its key can be dropped; then narrow, re-key, restore.
            migrationBuilder.Sql("ALTER TABLE `cheques` MODIFY COLUMN `id` int NOT NULL");
            migrationBuilder.Sql("ALTER TABLE `cheques` DROP PRIMARY KEY");
            migrationBuilder.Sql("CREATE INDEX `id` ON `cheques` (`id`)");
            migrationBuilder.Sql("ALTER TABLE `cheques` MODIFY COLUMN `id` int NOT NULL AUTO_INCREMENT");
        }
    }
}
