using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Smartnet.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Adopts <c>quotation_h</c> and <c>quotation_l</c> onto the new side, and adds the invoice→quotation
    /// back-link, so the documents engine can write quotations and convert them to invoices (Phase 5,
    /// slice 3).
    /// </summary>
    /// <remarks>
    /// <b>HAND-WRITTEN</b>, exactly like <c>Phase5AdoptInvoices</c>, and for the same reason. <c>dotnet ef
    /// migrations add</c> generated <c>CreateTable</c> for these — it cannot know they already exist in
    /// production — and, because the model snapshot had drifted, <c>AddColumn</c> for the invoice legacy
    /// varchar columns that <i>already exist</i> too. All of that is replaced by the additive SQL below.
    /// <b>If this migration is regenerated, it must be rewritten like this again.</b>
    ///
    /// <para><b>Additive, per DEVELOPMENT.md §8.</b> Every column added is nullable or defaulted, at the
    /// <i>end</i> of the table; nothing legacy is renamed, retyped, narrowed or dropped. <c>company_id</c>
    /// is <b>not</b> added because the multi-company migration already added it to <c>quotation_h</c>
    /// (nullable, backfilled from the legacy <c>company</c>); the shared <c>q_no</c>/<c>contactperson</c>/
    /// <c>q_valid</c>/<c>desc</c>/<c>itemcode</c> columns and every legacy <c>varchar</c> money/date column
    /// are left exactly as they are for the surviving legacy reader (a customer's quote history). The new
    /// app writes its own <c>decimal</c> columns beside them; the retype is Phase 9.</para>
    ///
    /// <para><b>One legacy write stops working, and that is expected</b> — the legacy positional
    /// <c>INSERT INTO quotation_h VALUES (…)</c> gets the wrong value count once columns are added, so it
    /// fails. Acceptable only because Phase 5 moves quotation creation to the new app and retires the
    /// legacy create screen: apply this migration and switch the quotation route together. Legacy
    /// <i>reads</i> are by column name and are unaffected.</para>
    ///
    /// <para>The two document tables now cross-reference: <c>invoice_h.source_quotation_id</c> points at
    /// the quotation an invoice was converted from, and <c>quotation_h.converted_to_invoice_id</c> at the
    /// invoice a quotation became. Both nullable, both plain scalar links (no FK constraint, to avoid a
    /// circular dependency between the two tables); the converter sets them atomically and refuses a
    /// second conversion.</para>
    /// </remarks>
    public partial class Phase5AdoptQuotations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // --- quotation_h: the new columns (company_id already exists), then the key it never had ---
            migrationBuilder.Sql("""
                ALTER TABLE `quotation_h`
                    ADD COLUMN `customer_id`             bigint        NOT NULL DEFAULT 0,
                    ADD COLUMN `quotation_date`          date          NOT NULL DEFAULT '1970-01-01',
                    ADD COLUMN `prepared_by`             bigint        NULL,
                    ADD COLUMN `subtotal`                decimal(18,4) NOT NULL DEFAULT 0,
                    ADD COLUMN `discount_percent`        decimal(18,4) NOT NULL DEFAULT 0,
                    ADD COLUMN `discount_amount`         decimal(18,4) NOT NULL DEFAULT 0,
                    ADD COLUMN `net_total`               decimal(18,4) NOT NULL DEFAULT 0,
                    ADD COLUMN `tax_rate_id`             bigint        NULL,
                    ADD COLUMN `tax_rate_percentage`     decimal(18,4) NOT NULL DEFAULT 0,
                    ADD COLUMN `tax_amount`              decimal(18,4) NOT NULL DEFAULT 0,
                    ADD COLUMN `total_amount`            decimal(18,4) NOT NULL DEFAULT 0,
                    ADD COLUMN `cost_amount`             decimal(18,4) NOT NULL DEFAULT 0,
                    ADD COLUMN `converted_to_invoice_id` bigint        NULL,
                    ADD COLUMN `converted_at`            datetime(6)   NULL,
                    ADD COLUMN `converted_by`            bigint        NULL,
                    ADD COLUMN `data_origin`             varchar(16)   NOT NULL DEFAULT 'legacy',
                    ADD COLUMN `created_by`              bigint        NULL,
                    ADD COLUMN `created_at`              datetime(6)   NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                    ADD COLUMN `updated_by`              bigint        NULL,
                    ADD COLUMN `updated_at`              datetime(6)   NULL,
                    ADD COLUMN `deleted_by`              bigint        NULL,
                    ADD COLUMN `deleted_at`              datetime(6)   NULL,
                    ADD COLUMN `row_version`             int           NOT NULL DEFAULT 1
                """);

            migrationBuilder.Sql("""
                ALTER TABLE `quotation_h`
                    ADD COLUMN `id` bigint NOT NULL AUTO_INCREMENT,
                    ADD PRIMARY KEY (`id`)
                """);

            // --- quotation_l: the same, plus the nullable link back to its header --------------------
            migrationBuilder.Sql("""
                ALTER TABLE `quotation_l`
                    ADD COLUMN `quotation_id`     bigint        NULL,
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
                ALTER TABLE `quotation_l`
                    ADD COLUMN `id` bigint NOT NULL AUTO_INCREMENT,
                    ADD PRIMARY KEY (`id`)
                """);

            migrationBuilder.Sql("CREATE INDEX `IX_quotation_l_quotation_id` ON `quotation_l` (`quotation_id`)");

            migrationBuilder.Sql("""
                ALTER TABLE `quotation_l`
                    ADD CONSTRAINT `FK_quotation_l_quotation_h_quotation_id`
                    FOREIGN KEY (`quotation_id`) REFERENCES `quotation_h` (`id`) ON DELETE RESTRICT
                """);

            // --- invoice_h: the back-link to the quotation an invoice was converted from --------------
            migrationBuilder.Sql("ALTER TABLE `invoice_h` ADD COLUMN `source_quotation_id` bigint NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // As in Phase5AdoptInvoices: Down() removes only what Up() added. The rows, company_id and the
            // legacy varchar columns predate this migration and outlive a rollback — no DropTable.
            migrationBuilder.Sql("ALTER TABLE `invoice_h` DROP COLUMN `source_quotation_id`");

            migrationBuilder.Sql("ALTER TABLE `quotation_l` DROP FOREIGN KEY `FK_quotation_l_quotation_h_quotation_id`");
            migrationBuilder.Sql("DROP INDEX `IX_quotation_l_quotation_id` ON `quotation_l`");

            // AUTO_INCREMENT has to stop being AUTO_INCREMENT before its key can go.
            migrationBuilder.Sql("ALTER TABLE `quotation_l` MODIFY COLUMN `id` bigint NOT NULL");
            migrationBuilder.Sql("ALTER TABLE `quotation_l` DROP PRIMARY KEY");
            migrationBuilder.Sql("ALTER TABLE `quotation_l` DROP COLUMN `id`");
            migrationBuilder.Sql("""
                ALTER TABLE `quotation_l`
                    DROP COLUMN `quotation_id`,
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

            migrationBuilder.Sql("ALTER TABLE `quotation_h` MODIFY COLUMN `id` bigint NOT NULL");
            migrationBuilder.Sql("ALTER TABLE `quotation_h` DROP PRIMARY KEY");
            migrationBuilder.Sql("ALTER TABLE `quotation_h` DROP COLUMN `id`");
            migrationBuilder.Sql("""
                ALTER TABLE `quotation_h`
                    DROP COLUMN `customer_id`,
                    DROP COLUMN `quotation_date`,
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
                    DROP COLUMN `converted_to_invoice_id`,
                    DROP COLUMN `converted_at`,
                    DROP COLUMN `converted_by`,
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
