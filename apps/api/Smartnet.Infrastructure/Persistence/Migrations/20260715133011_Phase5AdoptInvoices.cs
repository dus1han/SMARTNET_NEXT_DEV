using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Smartnet.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Adopts <c>invoice_h</c> and <c>invoice_l</c> onto the new side, so the documents engine can write
    /// invoices the legacy app still reads (Phase 5, slice 1).
    /// </summary>
    /// <remarks>
    /// <b>HAND-WRITTEN</b>, like <c>AdoptMasterData</c>, and for the same reason. <c>dotnet ef migrations
    /// add</c> generated a <c>CreateTable</c> for each of these — it cannot know they already exist — and
    /// a <c>DropTable</c> in <c>Down()</c>, which would have destroyed 2,485 invoices and 12,598 lines.
    /// All of that is replaced by the additive SQL below. <b>If this migration is regenerated, it must be
    /// rewritten like this again.</b>
    ///
    /// <para><b>Additive, per DEVELOPMENT.md §8.</b> Every column added is nullable or defaulted, at the
    /// <i>end</i> of the table; nothing legacy is renamed, retyped, narrowed or dropped. Two columns the
    /// entity maps are <b>not</b> added because they already exist: <c>company_id</c> (the multi-company
    /// migration added it, nullable, populated from the legacy <c>company</c>/<c>format</c> value — 2,485
    /// rows, zero nulls) and the shared <c>invoiceno</c>/<c>pono</c>/<c>contactperson</c>/<c>desc</c>/
    /// <c>itemcode</c> columns. The legacy <c>varchar</c> money columns (<c>totamount</c>, <c>balance</c>,
    /// …) are left exactly as they are for the legacy readers; the new app writes its own <c>decimal</c>
    /// columns beside them. The <c>varchar → DECIMAL</c> retype is Phase 9.</para>
    ///
    /// <para><b>One legacy write does stop working, and that is expected.</b> The legacy app raises an
    /// invoice with a <i>positional</i> <c>INSERT INTO invoice_h VALUES (…)</c> (no column list — the 23
    /// places DEVELOPMENT.md warns about). Adding columns makes that INSERT's value count wrong, so it
    /// fails. That is acceptable <b>only because Phase 5 moves invoice creation to the new app</b> and the
    /// legacy create screen is retired: apply this migration and switch the invoice route to the new stack
    /// together. Legacy <i>reads</i> are by column name and are unaffected.</para>
    ///
    /// <para>The new typed columns are <c>NOT NULL DEFAULT</c> so the 2,485 existing rows stay valid; those
    /// rows carry meaningless defaults on the new columns and are never read as an <c>Invoice</c> — the
    /// entity has a <c>data_origin = 'new'</c> query filter, and <c>data_origin</c> defaults to
    /// <c>'legacy'</c>. The 608 orphan lines (Finding 3) keep <c>invoice_id = NULL</c> under a nullable
    /// foreign key (LEGACY-DATA-POLICY §3); existing lines are <b>not</b> linked here — that is a data
    /// step, not this structural one.</para>
    /// </remarks>
    public partial class Phase5AdoptInvoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // --- invoice_h: the new columns (company_id already exists), then the key it never had ----
            migrationBuilder.Sql("""
                ALTER TABLE `invoice_h`
                    ADD COLUMN `customer_id`         bigint        NOT NULL DEFAULT 0,
                    ADD COLUMN `invoice_date`        date          NOT NULL DEFAULT '1970-01-01',
                    ADD COLUMN `invoice_type`        varchar(16)   NOT NULL DEFAULT 'Credit',
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
                ALTER TABLE `invoice_h`
                    ADD COLUMN `id` bigint NOT NULL AUTO_INCREMENT,
                    ADD PRIMARY KEY (`id`)
                """);

            // --- invoice_l: the same, plus the nullable link back to its header ----------------------
            migrationBuilder.Sql("""
                ALTER TABLE `invoice_l`
                    ADD COLUMN `invoice_id`       bigint        NULL,
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
                ALTER TABLE `invoice_l`
                    ADD COLUMN `id` bigint NOT NULL AUTO_INCREMENT,
                    ADD PRIMARY KEY (`id`)
                """);

            migrationBuilder.Sql("CREATE INDEX `IX_invoice_l_invoice_id` ON `invoice_l` (`invoice_id`)");

            // The one join the legacy schema never had. Nullable, so the 608 orphan lines are permitted
            // to keep a null parent rather than being deleted (LEGACY-DATA-POLICY §3). RESTRICT, because
            // an invoice with lines is soft-deleted, never torn out from under them.
            migrationBuilder.Sql("""
                ALTER TABLE `invoice_l`
                    ADD CONSTRAINT `FK_invoice_l_invoice_h_invoice_id`
                    FOREIGN KEY (`invoice_id`) REFERENCES `invoice_h` (`id`) ON DELETE RESTRICT
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Note what is absent: DropTable, and any drop of company_id. Down() removes only what Up()
            // added; the rows and company_id predate this migration and outlive a rollback.
            migrationBuilder.Sql("ALTER TABLE `invoice_l` DROP FOREIGN KEY `FK_invoice_l_invoice_h_invoice_id`");
            migrationBuilder.Sql("DROP INDEX `IX_invoice_l_invoice_id` ON `invoice_l`");

            // AUTO_INCREMENT has to stop being AUTO_INCREMENT before its key can go.
            migrationBuilder.Sql("ALTER TABLE `invoice_l` MODIFY COLUMN `id` bigint NOT NULL");
            migrationBuilder.Sql("ALTER TABLE `invoice_l` DROP PRIMARY KEY");
            migrationBuilder.Sql("ALTER TABLE `invoice_l` DROP COLUMN `id`");
            migrationBuilder.Sql("""
                ALTER TABLE `invoice_l`
                    DROP COLUMN `invoice_id`,
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

            migrationBuilder.Sql("ALTER TABLE `invoice_h` MODIFY COLUMN `id` bigint NOT NULL");
            migrationBuilder.Sql("ALTER TABLE `invoice_h` DROP PRIMARY KEY");
            migrationBuilder.Sql("ALTER TABLE `invoice_h` DROP COLUMN `id`");
            migrationBuilder.Sql("""
                ALTER TABLE `invoice_h`
                    DROP COLUMN `customer_id`,
                    DROP COLUMN `invoice_date`,
                    DROP COLUMN `invoice_type`,
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
