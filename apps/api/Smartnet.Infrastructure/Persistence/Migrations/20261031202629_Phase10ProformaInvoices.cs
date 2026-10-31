using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Smartnet.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase10ProformaInvoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "proforma_invoices",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    number = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    quotation_id = table.Column<long>(type: "bigint", nullable: false),
                    quotation_number = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    customer_id = table.Column<long>(type: "bigint", nullable: false),
                    proforma_date = table.Column<DateOnly>(type: "date", nullable: false),
                    contact_person = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    validity = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    prepared_by = table.Column<long>(type: "bigint", nullable: true),
                    subtotal = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    discount_percent = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    discount_amount = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    net_total = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    tax_rate_id = table.Column<long>(type: "bigint", nullable: true),
                    tax_rate_percentage = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    tax_amount = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    total_amount = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    converted_to_invoice_id = table.Column<long>(type: "bigint", nullable: true),
                    converted_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    converted_by = table.Column<long>(type: "bigint", nullable: true),
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
                    table.PrimaryKey("PK_proforma_invoices", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "proforma_invoice_lines",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    proforma_invoice_id = table.Column<long>(type: "bigint", nullable: false),
                    item_id = table.Column<long>(type: "bigint", nullable: true),
                    item_code = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    description = table.Column<string>(type: "text", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    quantity = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    unit_price = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    discount_percent = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    gross = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    net = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    cost = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
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
                    table.PrimaryKey("PK_proforma_invoice_lines", x => x.id);
                    table.ForeignKey(
                        name: "FK_proforma_invoice_lines_proforma_invoices_proforma_invoice_id",
                        column: x => x.proforma_invoice_id,
                        principalTable: "proforma_invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_proforma_invoice_lines_proforma_invoice_id",
                table: "proforma_invoice_lines",
                column: "proforma_invoice_id");

            migrationBuilder.CreateIndex(
                name: "IX_proforma_invoices_company_id_number",
                table: "proforma_invoices",
                columns: new[] { "company_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_proforma_invoices_converted_to_invoice_id",
                table: "proforma_invoices",
                column: "converted_to_invoice_id");

            migrationBuilder.CreateIndex(
                name: "IX_proforma_invoices_quotation_id",
                table: "proforma_invoices",
                column: "quotation_id");

            // A proforma numbering series for every company that already numbers quotations — each company
            // its own sequence, as with every document. The prefix is the quotation's with "Q-" turned into
            // "PI-" (STQ- → STPI-, SNQ- → SNPI-), falling back to plain "PI-"; an administrator can change it
            // under Settings → Numbering. New companies get theirs from CompanyProvisioner.
            migrationBuilder.Sql("""
                INSERT INTO document_series (company_id, doc_type, prefix, next_number, padding, created_at, row_version)
                SELECT q.company_id,
                       'PROFORMA',
                       CASE WHEN q.prefix LIKE '%Q-' THEN CONCAT(LEFT(q.prefix, CHAR_LENGTH(q.prefix) - 2), 'PI-') ELSE 'PI-' END,
                       1, 0, UTC_TIMESTAMP(6), 1
                FROM document_series q
                WHERE q.doc_type = 'QUOTATION'
                  AND NOT EXISTS (SELECT 1 FROM document_series p
                                  WHERE p.company_id = q.company_id AND p.doc_type = 'PROFORMA');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM document_series WHERE doc_type = 'PROFORMA';");

            migrationBuilder.DropTable(
                name: "proforma_invoice_lines");

            migrationBuilder.DropTable(
                name: "proforma_invoices");
        }
    }
}
