using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Smartnet.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Adopts <c>supplier_invoice</c> onto the new side and creates the new <c>payables_ledger</c> table
    /// (Phase 6, slice 2).
    /// </summary>
    /// <remarks>
    /// <b>Partly HAND-WRITTEN.</b> <c>dotnet ef migrations add</c> generated <c>CreateTable</c> for
    /// <c>supplier_invoice</c> — it cannot know the table already exists in production — which would drop the
    /// live AP history. That half is replaced by the additive adoption SQL below; the <c>payables_ledger</c>
    /// <c>CreateTable</c> is kept exactly as generated, because it <i>is</i> a genuinely new table.
    /// <b>If this migration is regenerated, the supplier_invoice half must be rewritten like this again.</b>
    ///
    /// <para><b>supplier_invoice is adopted like item_stock, not like invoice_h.</b> It already had an
    /// <c>int</c> <c>id</c> — but AUTO_INCREMENT under a non-unique <c>KEY</c>, not a primary key (Finding
    /// 6). So the adoption <i>promotes</i> that id: widens it to <c>bigint</c>, makes it the primary key, and
    /// drops the redundant key — rather than adding a new one. <c>company_id</c> already exists (the
    /// multi-company migration added it), and every legacy <c>varchar</c> column (<c>invno</c>, <c>supcode</c>,
    /// <c>amount</c>, <c>paymentstat</c>, <c>invdate</c>, <c>company</c>, <c>novattotal</c>, <c>vtype</c>,
    /// <c>vper</c>) is left exactly as it is for the surviving legacy readers (the supplier reports); the new
    /// app writes its own <c>decimal</c>/<c>date</c> columns beside them.</para>
    ///
    /// <para><c>supplier_inv_pay</c> is <b>not</b> adopted — the new app's payments live in the payables
    /// ledger, and a row is dual-written to that legacy table for the legacy supplier-payment report.</para>
    /// </remarks>
    public partial class Phase6SupplierInvoicesAndPayables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // --- supplier_invoice: promote the existing int id to a bigint primary key ----------------
            migrationBuilder.Sql("ALTER TABLE `supplier_invoice` MODIFY COLUMN `id` bigint NOT NULL AUTO_INCREMENT");
            migrationBuilder.Sql("ALTER TABLE `supplier_invoice` ADD PRIMARY KEY (`id`)");
            migrationBuilder.Sql("ALTER TABLE `supplier_invoice` DROP INDEX `id`");

            // --- supplier_invoice: the new columns (company_id and the legacy varchars already exist) --
            migrationBuilder.Sql("""
                ALTER TABLE `supplier_invoice`
                    ADD COLUMN `supplier_id`         bigint        NOT NULL DEFAULT 0,
                    ADD COLUMN `invoice_date`        date          NOT NULL DEFAULT '1970-01-01',
                    ADD COLUMN `net_total`           decimal(18,4) NOT NULL DEFAULT 0,
                    ADD COLUMN `tax_rate_percentage` decimal(18,4) NOT NULL DEFAULT 0,
                    ADD COLUMN `total_amount`        decimal(18,4) NOT NULL DEFAULT 0,
                    ADD COLUMN `data_origin`         varchar(16)   NOT NULL DEFAULT 'legacy',
                    ADD COLUMN `created_by`          bigint        NULL,
                    ADD COLUMN `created_at`          datetime(6)   NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                    ADD COLUMN `updated_by`          bigint        NULL,
                    ADD COLUMN `updated_at`          datetime(6)   NULL,
                    ADD COLUMN `deleted_by`          bigint        NULL,
                    ADD COLUMN `deleted_at`          datetime(6)   NULL,
                    ADD COLUMN `row_version`         int           NOT NULL DEFAULT 1
                """);

            // --- payables_ledger: a genuinely new table — EF's generated CreateTable, kept as-is --------
            migrationBuilder.CreateTable(
                name: "payables_ledger",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    supplier_id = table.Column<long>(type: "bigint", nullable: false),
                    type = table.Column<string>(type: "varchar(24)", maxLength: 24, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    amount = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    supplier_invoice_id = table.Column<long>(type: "bigint", nullable: true),
                    occurred_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    note = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
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
                    table.PrimaryKey("PK_payables_ledger", x => x.id);
                    table.ForeignKey(
                        name: "FK_payables_ledger_sup_m_supplier_id",
                        column: x => x.supplier_id,
                        principalTable: "sup_m",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_payables_ledger_supplier_invoice_supplier_invoice_id",
                        column: x => x.supplier_invoice_id,
                        principalTable: "supplier_invoice",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_payables_ledger_supplier_id",
                table: "payables_ledger",
                column: "supplier_id");

            migrationBuilder.CreateIndex(
                name: "IX_payables_ledger_supplier_invoice_id",
                table: "payables_ledger",
                column: "supplier_invoice_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // payables_ledger is a new table — drop it (this also removes its FK to supplier_invoice).
            migrationBuilder.DropTable(
                name: "payables_ledger");

            // Reverse the supplier_invoice adoption: drop the new columns, then restore the original
            // int-AUTO_INCREMENT-under-a-KEY id. company_id and the legacy varchars predate this migration.
            migrationBuilder.Sql("""
                ALTER TABLE `supplier_invoice`
                    DROP COLUMN `supplier_id`,
                    DROP COLUMN `invoice_date`,
                    DROP COLUMN `net_total`,
                    DROP COLUMN `tax_rate_percentage`,
                    DROP COLUMN `total_amount`,
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
            migrationBuilder.Sql("ALTER TABLE `supplier_invoice` MODIFY COLUMN `id` int NOT NULL");
            migrationBuilder.Sql("ALTER TABLE `supplier_invoice` DROP PRIMARY KEY");
            migrationBuilder.Sql("CREATE INDEX `id` ON `supplier_invoice` (`id`)");
            migrationBuilder.Sql("ALTER TABLE `supplier_invoice` MODIFY COLUMN `id` int NOT NULL AUTO_INCREMENT");
        }
    }
}
