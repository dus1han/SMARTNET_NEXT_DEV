using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Smartnet.Domain.Identity;

#nullable disable

namespace Smartnet.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase10SuspendedReceivables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "suspended_receivables",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    customer_id = table.Column<long>(type: "bigint", nullable: false),
                    invoice_id = table.Column<long>(type: "bigint", nullable: false),
                    invoice_number = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    amount = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    suspended_on = table.Column<DateOnly>(type: "date", nullable: false),
                    reason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    status = table.Column<string>(type: "varchar(24)", maxLength: 24, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    reinstated_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    reinstated_by = table.Column<long>(type: "bigint", nullable: true),
                    reinstate_note = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    receipt_id = table.Column<long>(type: "bigint", nullable: true),
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
                    table.PrimaryKey("PK_suspended_receivables", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_suspended_receivables_customer_id",
                table: "suspended_receivables",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "IX_suspended_receivables_invoice_id_status",
                table: "suspended_receivables",
                columns: new[] { "invoice_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_suspended_receivables_status_company_id",
                table: "suspended_receivables",
                columns: new[] { "status", "company_id" });

            // The two administrator roles hold every grant but one (Permissions.AdministratorGrant), so they
            // take the new one too. Guarded, because a database built from scratch already granted it when the
            // roles were first seeded from that same list.
            migrationBuilder.Sql($"""
                INSERT INTO role_permissions (role_id, permission)
                SELECT r.id, '{Permissions.ReceivablesSuspend}'
                FROM roles r
                WHERE r.name IN ('{Role.DevAdmin}', '{Role.CompanyAdmin}')
                  AND r.deleted_at IS NULL
                  AND NOT EXISTS (
                      SELECT 1 FROM role_permissions rp
                      WHERE rp.role_id = r.id AND rp.permission = '{Permissions.ReceivablesSuspend}');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"DELETE FROM role_permissions WHERE permission = '{Permissions.ReceivablesSuspend}';");

            migrationBuilder.DropTable(
                name: "suspended_receivables");
        }
    }
}
