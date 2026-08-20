using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Smartnet.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Expenses become payable: recorded when incurred, settled afterwards in one payment or several
    /// (<c>expense_payments</c>), with the outstanding derived rather than stored.
    /// </summary>
    /// <remarks>
    /// Every expense that already exists was paid as it was entered — that was the only way to record one —
    /// so each live row is backfilled with a single settlement for its full amount, carrying the method,
    /// reference and date it was entered with. Without it every historical expense would read as unpaid.
    /// <para>The backfilled rows are marked <c>migrated</c>, and that origin is load-bearing: those expenses
    /// posted Dr the category + Input VAT, Cr Cash/Bank at the moment they were recorded, so their
    /// settlements must post nothing. Posting a payment entry for them would take the money out twice.</para>
    /// </remarks>
    public partial class Phase9ExpenseSettlement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "expense_payments",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    expense_id = table.Column<long>(type: "bigint", nullable: false),
                    company_id = table.Column<long>(type: "bigint", nullable: true),
                    paid_on = table.Column<DateOnly>(type: "date", nullable: false),
                    amount = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    method = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    reference = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    data_origin = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
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
                    table.PrimaryKey("PK_expense_payments", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_expense_payments_expense_id",
                table: "expense_payments",
                column: "expense_id");

            migrationBuilder.Sql(BackfillSql);
        }

        /// <summary>
        /// Backfill: every live expense was paid as it was entered, so each gets one settlement for its full
        /// amount. This app's rows carry typed columns; the adopted legacy ones keep their money and date in
        /// varchars, which are read defensively here — a value that is not a plain number becomes 0 (and so
        /// is skipped) rather than failing the migration, the same rule <c>LegacyValue</c> applies in code.
        /// </summary>
        /// <remarks>
        /// Public, and frozen like the rest of a migration, so a test can run this exact statement against
        /// seeded rows. A backfill that touches every historical row is worth proving, and proving it against
        /// a copy of the SQL would only test the copy.
        /// </remarks>
        public const string BackfillSql =
                """
                INSERT INTO `expense_payments`
                    (`expense_id`, `company_id`, `paid_on`, `amount`, `method`, `reference`,
                     `data_origin`, `created_at`, `row_version`)
                SELECT `expense_id`, `company_id`, `paid_on`, `amount`, `method`, `reference`,
                       'migrated', UTC_TIMESTAMP(6), 1
                FROM (
                    SELECT
                        e.`id` AS `expense_id`,
                        COALESCE(e.`company_id`, NULLIF(CAST(NULLIF(TRIM(e.`company`), '') AS UNSIGNED), 0)) AS `company_id`,
                        CASE
                            WHEN e.`data_origin` = 'new' THEN e.`spent_on`
                            ELSE COALESCE(STR_TO_DATE(NULLIF(TRIM(e.`expense_date`), ''), '%Y-%m-%d'), e.`spent_on`)
                        END AS `paid_on`,
                        CASE
                            WHEN e.`data_origin` = 'new' THEN e.`amount`
                            WHEN REPLACE(TRIM(e.`expense_amount`), ',', '') REGEXP '^-?[0-9]*[.]?[0-9]+$'
                                THEN CAST(REPLACE(TRIM(e.`expense_amount`), ',', '') AS DECIMAL(18,4))
                            ELSE 0
                        END AS `amount`,
                        NULLIF(TRIM(e.`paymentm`), '') AS `method`,
                        NULLIF(TRIM(e.`payment_ref`), '') AS `reference`
                    FROM `expense_tr` e
                    WHERE e.`deleted_at` IS NULL
                ) settled
                WHERE `amount` > 0
                """;

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "expense_payments");
        }
    }
}
