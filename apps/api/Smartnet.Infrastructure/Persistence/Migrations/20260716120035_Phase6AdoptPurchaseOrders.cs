using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Smartnet.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Adopts <c>po_h</c> and <c>po_l</c> onto the new side, so the documents engine can write purchase
    /// orders (Phase 6, slice 1).
    /// </summary>
    /// <remarks>
    /// <b>HAND-WRITTEN</b>, exactly like <c>Phase5AdoptInvoices</c>/<c>Phase5AdoptQuotations</c>, and for
    /// the same reason. <c>dotnet ef migrations add</c> generated <c>CreateTable</c> for these — it cannot
    /// know they already exist in production — which would drop the live PO history. All of that is
    /// replaced by the additive SQL below. <b>If this migration is regenerated, it must be rewritten like
    /// this again.</b>
    ///
    /// <para><b>Additive, per DEVELOPMENT.md §8.</b> Every column added is nullable or defaulted, at the
    /// <i>end</i> of the table; nothing legacy is renamed, retyped, narrowed or dropped. <c>company_id</c>
    /// is <b>not</b> added because the multi-company migration already added it to <c>po_h</c> (nullable,
    /// backfilled from the legacy <c>company</c>); the shared <c>po_no</c> and <c>desc</c> columns and
    /// every legacy <c>varchar</c> money/date column (<c>podate</c>, <c>supplier</c>, <c>totamount</c>,
    /// <c>preparedby</c>, <c>cdatetime</c>, <c>company</c>, <c>nonvattotal</c>, <c>vatty</c>,
    /// <c>vatpercent</c>) are left exactly as they are for the surviving legacy readers (SearchPO's list,
    /// the PO reprint). The new app writes its own <c>decimal</c> columns beside them; the retype is
    /// Phase 9.</para>
    ///
    /// <para><b>One legacy write stops working, and that is expected</b> — as with the Phase 5 adoptions:
    /// Phase 6 moves PO creation to the new app and retires the legacy create screen, so the two are
    /// switched together. Legacy <i>reads</i> are by column name and are unaffected.</para>
    ///
    /// <para><c>po_l</c> gains a real, nullable foreign key to <c>po_h</c>'s new surrogate id — nullable so
    /// any legacy orphan line keeps a null parent rather than being deleted (LEGACY-DATA-POLICY §3);
    /// <c>item_id</c>/<c>item_code</c>/<c>cost</c> are new because the legacy PO line was free text.</para>
    /// </remarks>
    public partial class Phase6AdoptPurchaseOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // --- po_h: the new columns (company_id and the legacy varchars already exist), then the key ---
            migrationBuilder.Sql("""
                ALTER TABLE `po_h`
                    ADD COLUMN `supplier_id`         bigint        NOT NULL DEFAULT 0,
                    ADD COLUMN `po_date`             date          NOT NULL DEFAULT '1970-01-01',
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
                ALTER TABLE `po_h`
                    ADD COLUMN `id` bigint NOT NULL AUTO_INCREMENT,
                    ADD PRIMARY KEY (`id`)
                """);

            // --- po_l: the same, plus the nullable link back to its header ---------------------------
            migrationBuilder.Sql("""
                ALTER TABLE `po_l`
                    ADD COLUMN `purchase_order_id` bigint        NULL,
                    ADD COLUMN `item_id`           bigint        NULL,
                    ADD COLUMN `item_code`         varchar(100)  NULL,
                    ADD COLUMN `quantity`          decimal(18,4) NOT NULL DEFAULT 0,
                    ADD COLUMN `unit_price`        decimal(18,4) NOT NULL DEFAULT 0,
                    ADD COLUMN `discount_percent`  decimal(18,4) NOT NULL DEFAULT 0,
                    ADD COLUMN `gross`             decimal(18,4) NOT NULL DEFAULT 0,
                    ADD COLUMN `net`               decimal(18,4) NOT NULL DEFAULT 0,
                    ADD COLUMN `cost`              decimal(18,4) NULL,
                    ADD COLUMN `created_by`        bigint        NULL,
                    ADD COLUMN `created_at`        datetime(6)   NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                    ADD COLUMN `updated_by`        bigint        NULL,
                    ADD COLUMN `updated_at`        datetime(6)   NULL,
                    ADD COLUMN `deleted_by`        bigint        NULL,
                    ADD COLUMN `deleted_at`        datetime(6)   NULL,
                    ADD COLUMN `row_version`       int           NOT NULL DEFAULT 1
                """);

            migrationBuilder.Sql("""
                ALTER TABLE `po_l`
                    ADD COLUMN `id` bigint NOT NULL AUTO_INCREMENT,
                    ADD PRIMARY KEY (`id`)
                """);

            migrationBuilder.Sql("CREATE INDEX `IX_po_l_purchase_order_id` ON `po_l` (`purchase_order_id`)");

            migrationBuilder.Sql("""
                ALTER TABLE `po_l`
                    ADD CONSTRAINT `FK_po_l_po_h_purchase_order_id`
                    FOREIGN KEY (`purchase_order_id`) REFERENCES `po_h` (`id`) ON DELETE RESTRICT
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // As in the Phase 5 adoptions: Down() removes only what Up() added. The rows, company_id and
            // the legacy varchar columns predate this migration and outlive a rollback — no DropTable.
            migrationBuilder.Sql("ALTER TABLE `po_l` DROP FOREIGN KEY `FK_po_l_po_h_purchase_order_id`");
            migrationBuilder.Sql("DROP INDEX `IX_po_l_purchase_order_id` ON `po_l`");

            // AUTO_INCREMENT has to stop being AUTO_INCREMENT before its key can go.
            migrationBuilder.Sql("ALTER TABLE `po_l` MODIFY COLUMN `id` bigint NOT NULL");
            migrationBuilder.Sql("ALTER TABLE `po_l` DROP PRIMARY KEY");
            migrationBuilder.Sql("ALTER TABLE `po_l` DROP COLUMN `id`");
            migrationBuilder.Sql("""
                ALTER TABLE `po_l`
                    DROP COLUMN `purchase_order_id`,
                    DROP COLUMN `item_id`,
                    DROP COLUMN `item_code`,
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

            migrationBuilder.Sql("ALTER TABLE `po_h` MODIFY COLUMN `id` bigint NOT NULL");
            migrationBuilder.Sql("ALTER TABLE `po_h` DROP PRIMARY KEY");
            migrationBuilder.Sql("ALTER TABLE `po_h` DROP COLUMN `id`");
            migrationBuilder.Sql("""
                ALTER TABLE `po_h`
                    DROP COLUMN `supplier_id`,
                    DROP COLUMN `po_date`,
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
