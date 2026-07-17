using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Smartnet.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase7AdoptExpenses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Adopt exp_cat_m and expense_tr additively (Phase 7, slice 3).

            // --- exp_cat_m: promote the AUTO_INCREMENT id under a non-unique key to a real PK, add audit ---
            migrationBuilder.Sql("ALTER TABLE `exp_cat_m` MODIFY COLUMN `id` bigint NOT NULL AUTO_INCREMENT");
            migrationBuilder.Sql("ALTER TABLE `exp_cat_m` ADD PRIMARY KEY (`id`)");
            migrationBuilder.Sql("ALTER TABLE `exp_cat_m` DROP INDEX `id`");
            migrationBuilder.Sql("""
                ALTER TABLE `exp_cat_m`
                    ADD COLUMN `created_by`  bigint      NULL,
                    ADD COLUMN `created_at`  datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                    ADD COLUMN `updated_by`  bigint      NULL,
                    ADD COLUMN `updated_at`  datetime(6) NULL,
                    ADD COLUMN `deleted_by`  bigint      NULL,
                    ADD COLUMN `deleted_at`  datetime(6) NULL,
                    ADD COLUMN `row_version` int         NOT NULL DEFAULT 1
                """);

            // --- expense_tr: the legacy `id` was 0 on every row — drop it for a real surrogate (like invoice_h),
            // then add the typed columns. company_id and the legacy varchars already exist. -----------------
            migrationBuilder.Sql("ALTER TABLE `expense_tr` DROP COLUMN `id`");
            migrationBuilder.Sql("ALTER TABLE `expense_tr` ADD COLUMN `id` bigint NOT NULL AUTO_INCREMENT PRIMARY KEY FIRST");
            migrationBuilder.Sql("""
                ALTER TABLE `expense_tr`
                    ADD COLUMN `category_id`  bigint        NOT NULL DEFAULT 0,
                    ADD COLUMN `amount`       decimal(18,4) NOT NULL DEFAULT 0,
                    ADD COLUMN `spent_on`     date          NOT NULL DEFAULT '1970-01-01',
                    ADD COLUMN `data_origin`  varchar(16)   NOT NULL DEFAULT 'legacy',
                    ADD COLUMN `created_by`   bigint        NULL,
                    ADD COLUMN `created_at`   datetime(6)   NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                    ADD COLUMN `updated_by`   bigint        NULL,
                    ADD COLUMN `updated_at`   datetime(6)   NULL,
                    ADD COLUMN `deleted_by`   bigint        NULL,
                    ADD COLUMN `deleted_at`   datetime(6)   NULL,
                    ADD COLUMN `row_version`  int           NOT NULL DEFAULT 1
                """);
            migrationBuilder.Sql("CREATE INDEX `IX_expense_tr_category_id` ON `expense_tr` (`category_id`)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX `IX_expense_tr_category_id` ON `expense_tr`");
            migrationBuilder.Sql("""
                ALTER TABLE `expense_tr`
                    DROP COLUMN `category_id`,
                    DROP COLUMN `amount`,
                    DROP COLUMN `spent_on`,
                    DROP COLUMN `data_origin`,
                    DROP COLUMN `created_by`,
                    DROP COLUMN `created_at`,
                    DROP COLUMN `updated_by`,
                    DROP COLUMN `updated_at`,
                    DROP COLUMN `deleted_by`,
                    DROP COLUMN `deleted_at`,
                    DROP COLUMN `row_version`
                """);
            migrationBuilder.Sql("ALTER TABLE `expense_tr` DROP COLUMN `id`");
            migrationBuilder.Sql("ALTER TABLE `expense_tr` ADD COLUMN `id` int NOT NULL DEFAULT 0 FIRST");

            migrationBuilder.Sql("""
                ALTER TABLE `exp_cat_m`
                    DROP COLUMN `created_by`,
                    DROP COLUMN `created_at`,
                    DROP COLUMN `updated_by`,
                    DROP COLUMN `updated_at`,
                    DROP COLUMN `deleted_by`,
                    DROP COLUMN `deleted_at`,
                    DROP COLUMN `row_version`
                """);
            migrationBuilder.Sql("ALTER TABLE `exp_cat_m` MODIFY COLUMN `id` int NOT NULL");
            migrationBuilder.Sql("ALTER TABLE `exp_cat_m` DROP PRIMARY KEY");
            migrationBuilder.Sql("CREATE INDEX `id` ON `exp_cat_m` (`id`)");
            migrationBuilder.Sql("ALTER TABLE `exp_cat_m` MODIFY COLUMN `id` int NOT NULL AUTO_INCREMENT");
        }
    }
}
