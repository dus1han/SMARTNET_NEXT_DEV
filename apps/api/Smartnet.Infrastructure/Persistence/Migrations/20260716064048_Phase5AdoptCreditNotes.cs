using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Smartnet.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Adopts <c>cn_h</c> and <c>cn_l</c> onto the new side, so the documents engine can write credit notes
    /// (Phase 5, slice 4).
    /// </summary>
    /// <remarks>
    /// <b>HAND-WRITTEN</b>, exactly like <c>Phase5AdoptInvoices</c> and <c>Phase5AdoptQuotations</c>, and for
    /// the same reason. <c>dotnet ef migrations add</c> generated <c>CreateTable</c> for these — it cannot
    /// know they already exist in production — and would re-add <c>company_id</c>, which the multi-company
    /// migration already added to <c>cn_h</c>. All of that is replaced by the additive SQL below. <b>If this
    /// migration is regenerated, it must be rewritten like this again.</b>
    ///
    /// <para><b>Additive, per DEVELOPMENT.md §8.</b> Every column added is nullable or defaulted, at the
    /// <i>end</i> of the table; nothing legacy is renamed, retyped, narrowed or dropped. <c>company_id</c> is
    /// <b>not</b> added because the multi-company migration already added it to <c>cn_h</c> (nullable,
    /// backfilled from the parent invoice); the legacy money/date/flag columns (<c>cnno</c>, <c>invoiceno</c>,
    /// <c>cndate</c>, <c>totamount</c>, <c>novattotal</c>, <c>vper</c>, <c>vtype</c>, <c>stockposting</c>, …)
    /// are left exactly as they are. The new app writes its own <c>decimal</c> columns beside them; the
    /// retype is Phase 9.</para>
    ///
    /// <para>The typed <c>invoice_id</c> is the surrogate parent link the legacy <c>invoiceno</c> string only
    /// approximated — a plain scalar column (no FK constraint, since the parent can be a legacy invoice row
    /// whose surrogate id lives in the same adopted <c>invoice_h</c>). <c>cn_h</c> has no surviving legacy
    /// reader once Phase 5 retires the legacy credit-note screens, but the two NOT NULL legacy columns
    /// (<c>invoiceno</c>, <c>stockposting</c>) are still written on save so the insert satisfies the shape.</para>
    ///
    /// <para><b>One legacy write stops working, and that is expected</b> — the legacy positional
    /// <c>INSERT INTO cn_h VALUES (…)</c> gets the wrong value count once columns are added, so it fails.
    /// Acceptable only because Phase 5 moves credit-note creation to the new app and retires the legacy
    /// screen: apply this migration and switch the credit-note route together. Legacy <i>reads</i> are by
    /// column name and are unaffected.</para>
    /// </remarks>
    public partial class Phase5AdoptCreditNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // --- cn_h: the new columns (company_id already exists), then the key it never had ----------
            migrationBuilder.Sql("""
                ALTER TABLE `cn_h`
                    ADD COLUMN `customer_id`         bigint        NOT NULL DEFAULT 0,
                    ADD COLUMN `invoice_id`          bigint        NOT NULL DEFAULT 0,
                    ADD COLUMN `credit_note_date`    date          NOT NULL DEFAULT '1970-01-01',
                    ADD COLUMN `returns_stock`       tinyint(1)    NOT NULL DEFAULT 0,
                    ADD COLUMN `prepared_by`         bigint        NULL,
                    ADD COLUMN `subtotal`            decimal(18,4) NOT NULL DEFAULT 0,
                    ADD COLUMN `discount_percent`    decimal(18,4) NOT NULL DEFAULT 0,
                    ADD COLUMN `discount_amount`     decimal(18,4) NOT NULL DEFAULT 0,
                    ADD COLUMN `net_total`           decimal(18,4) NOT NULL DEFAULT 0,
                    ADD COLUMN `tax_rate_id`         bigint        NULL,
                    ADD COLUMN `tax_rate_percentage` decimal(18,4) NOT NULL DEFAULT 0,
                    ADD COLUMN `tax_amount`          decimal(18,4) NOT NULL DEFAULT 0,
                    ADD COLUMN `total_amount`        decimal(18,4) NOT NULL DEFAULT 0,
                    ADD COLUMN `cost_amount`         decimal(18,4) NOT NULL DEFAULT 0,
                    ADD COLUMN `data_origin`         varchar(16)   NOT NULL DEFAULT 'legacy',
                    ADD COLUMN `created_by`          bigint        NULL,
                    ADD COLUMN `created_at`          datetime(6)   NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                    ADD COLUMN `updated_by`          bigint        NULL,
                    ADD COLUMN `updated_at`          datetime(6)   NULL,
                    ADD COLUMN `deleted_by`          bigint        NULL,
                    ADD COLUMN `deleted_at`          datetime(6)   NULL,
                    ADD COLUMN `row_version`         int           NOT NULL DEFAULT 1
                """);

            migrationBuilder.Sql("""
                ALTER TABLE `cn_h`
                    ADD COLUMN `id` bigint NOT NULL AUTO_INCREMENT,
                    ADD PRIMARY KEY (`id`)
                """);

            // --- cn_l: the same, plus the nullable link back to its header ----------------------------
            migrationBuilder.Sql("""
                ALTER TABLE `cn_l`
                    ADD COLUMN `credit_note_id`   bigint        NULL,
                    ADD COLUMN `item_id`          bigint        NULL,
                    ADD COLUMN `quantity`         decimal(18,4) NOT NULL DEFAULT 0,
                    ADD COLUMN `unit_price`       decimal(18,4) NOT NULL DEFAULT 0,
                    ADD COLUMN `discount_percent` decimal(18,4) NOT NULL DEFAULT 0,
                    ADD COLUMN `gross`            decimal(18,4) NOT NULL DEFAULT 0,
                    ADD COLUMN `net`              decimal(18,4) NOT NULL DEFAULT 0,
                    ADD COLUMN `cost`             decimal(18,4) NULL,
                    ADD COLUMN `created_by`       bigint        NULL,
                    ADD COLUMN `created_at`       datetime(6)   NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                    ADD COLUMN `updated_by`       bigint        NULL,
                    ADD COLUMN `updated_at`       datetime(6)   NULL,
                    ADD COLUMN `deleted_by`       bigint        NULL,
                    ADD COLUMN `deleted_at`       datetime(6)   NULL,
                    ADD COLUMN `row_version`      int           NOT NULL DEFAULT 1
                """);

            migrationBuilder.Sql("""
                ALTER TABLE `cn_l`
                    ADD COLUMN `id` bigint NOT NULL AUTO_INCREMENT,
                    ADD PRIMARY KEY (`id`)
                """);

            migrationBuilder.Sql("CREATE INDEX `IX_cn_l_credit_note_id` ON `cn_l` (`credit_note_id`)");

            migrationBuilder.Sql("""
                ALTER TABLE `cn_l`
                    ADD CONSTRAINT `FK_cn_l_cn_h_credit_note_id`
                    FOREIGN KEY (`credit_note_id`) REFERENCES `cn_h` (`id`) ON DELETE RESTRICT
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // As in Phase5AdoptInvoices/Quotations: Down() removes only what Up() added. The rows, company_id
            // and the legacy varchar columns predate this migration and outlive a rollback — no DropTable.
            migrationBuilder.Sql("ALTER TABLE `cn_l` DROP FOREIGN KEY `FK_cn_l_cn_h_credit_note_id`");
            migrationBuilder.Sql("DROP INDEX `IX_cn_l_credit_note_id` ON `cn_l`");

            // AUTO_INCREMENT has to stop being AUTO_INCREMENT before its key can go.
            migrationBuilder.Sql("ALTER TABLE `cn_l` MODIFY COLUMN `id` bigint NOT NULL");
            migrationBuilder.Sql("ALTER TABLE `cn_l` DROP PRIMARY KEY");
            migrationBuilder.Sql("ALTER TABLE `cn_l` DROP COLUMN `id`");
            migrationBuilder.Sql("""
                ALTER TABLE `cn_l`
                    DROP COLUMN `credit_note_id`,
                    DROP COLUMN `item_id`,
                    DROP COLUMN `quantity`,
                    DROP COLUMN `unit_price`,
                    DROP COLUMN `discount_percent`,
                    DROP COLUMN `gross`,
                    DROP COLUMN `net`,
                    DROP COLUMN `cost`,
                    DROP COLUMN `created_by`,
                    DROP COLUMN `created_at`,
                    DROP COLUMN `updated_by`,
                    DROP COLUMN `updated_at`,
                    DROP COLUMN `deleted_by`,
                    DROP COLUMN `deleted_at`,
                    DROP COLUMN `row_version`
                """);

            migrationBuilder.Sql("ALTER TABLE `cn_h` MODIFY COLUMN `id` bigint NOT NULL");
            migrationBuilder.Sql("ALTER TABLE `cn_h` DROP PRIMARY KEY");
            migrationBuilder.Sql("ALTER TABLE `cn_h` DROP COLUMN `id`");
            migrationBuilder.Sql("""
                ALTER TABLE `cn_h`
                    DROP COLUMN `customer_id`,
                    DROP COLUMN `invoice_id`,
                    DROP COLUMN `credit_note_date`,
                    DROP COLUMN `returns_stock`,
                    DROP COLUMN `prepared_by`,
                    DROP COLUMN `subtotal`,
                    DROP COLUMN `discount_percent`,
                    DROP COLUMN `discount_amount`,
                    DROP COLUMN `net_total`,
                    DROP COLUMN `tax_rate_id`,
                    DROP COLUMN `tax_rate_percentage`,
                    DROP COLUMN `tax_amount`,
                    DROP COLUMN `total_amount`,
                    DROP COLUMN `cost_amount`,
                    DROP COLUMN `data_origin`,
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
