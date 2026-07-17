using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Smartnet.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase8GeneralLedgerAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "gl_accounts",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    code = table.Column<string>(type: "varchar(24)", maxLength: 24, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    type = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    is_cash_or_bank = table.Column<bool>(type: "tinyint(1)", nullable: false),
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
                    table.PrimaryKey("PK_gl_accounts", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "gl_entries",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    entry_date = table.Column<DateOnly>(type: "date", nullable: false),
                    source_type = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    source_id = table.Column<long>(type: "bigint", nullable: false),
                    description = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
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
                    table.PrimaryKey("PK_gl_entries", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "gl_lines",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    gl_entry_id = table.Column<long>(type: "bigint", nullable: true),
                    account_id = table.Column<long>(type: "bigint", nullable: false),
                    debit = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    credit = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
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
                    table.PrimaryKey("PK_gl_lines", x => x.id);
                    table.ForeignKey(
                        name: "FK_gl_lines_gl_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "gl_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_gl_lines_gl_entries_gl_entry_id",
                        column: x => x.gl_entry_id,
                        principalTable: "gl_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_gl_accounts_company_id_code",
                table: "gl_accounts",
                columns: new[] { "company_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_gl_entries_company_id",
                table: "gl_entries",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_gl_entries_source_type_source_id",
                table: "gl_entries",
                columns: new[] { "source_type", "source_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_gl_lines_account_id",
                table: "gl_lines",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "IX_gl_lines_gl_entry_id",
                table: "gl_lines",
                column: "gl_entry_id");

            // Seed the standard chart of accounts for every company (see GlAccountCodes).
            migrationBuilder.Sql("""
                INSERT INTO `gl_accounts` (`company_id`, `code`, `name`, `type`, `is_cash_or_bank`, `created_at`, `row_version`)
                SELECT c.`id`, x.code, x.name, x.type, x.cash, NOW(6), 1
                FROM `companies_m` c
                CROSS JOIN (
                                SELECT '1000' AS code, 'Cash'                AS name, 'Asset'     AS type, 1 AS cash
                    UNION ALL   SELECT '1010',          'Bank',                    'Asset',           1
                    UNION ALL   SELECT '1100',          'Accounts Receivable',     'Asset',           0
                    UNION ALL   SELECT '1200',          'Input VAT',               'Asset',           0
                    UNION ALL   SELECT '2000',          'Accounts Payable',        'Liability',       0
                    UNION ALL   SELECT '2100',          'Output VAT',              'Liability',       0
                    UNION ALL   SELECT '4000',          'Sales',                   'Income',          0
                    UNION ALL   SELECT '5000',          'Purchases',               'Expense',         0
                ) x
                """);

            // One expense account per category, per company (a categorised P&L). New categories get their
            // account on demand when an expense first posts (the posting engine creates it).
            migrationBuilder.Sql("""
                INSERT INTO `gl_accounts` (`company_id`, `code`, `name`, `type`, `is_cash_or_bank`, `created_at`, `row_version`)
                SELECT c.`id`, CONCAT('5-', cat.`id`), COALESCE(cat.`expcatname`, CONCAT('Category ', cat.`id`)), 'Expense', 0, NOW(6), 1
                FROM `companies_m` c
                CROSS JOIN `exp_cat_m` cat
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "gl_lines");

            migrationBuilder.DropTable(
                name: "gl_accounts");

            migrationBuilder.DropTable(
                name: "gl_entries");
        }
    }
}
