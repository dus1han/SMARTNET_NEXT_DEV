using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Smartnet.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase7ChequeSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "source_id",
                table: "cheques",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source_type",
                table: "cheques",
                type: "varchar(24)",
                maxLength: 24,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_cheques_source_type_source_id",
                table: "cheques",
                columns: new[] { "source_type", "source_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_cheques_source_type_source_id",
                table: "cheques");

            migrationBuilder.DropColumn(
                name: "source_id",
                table: "cheques");

            migrationBuilder.DropColumn(
                name: "source_type",
                table: "cheques");
        }
    }
}
