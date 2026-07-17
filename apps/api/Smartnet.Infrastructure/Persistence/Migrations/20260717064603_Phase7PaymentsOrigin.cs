using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Smartnet.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase7PaymentsOrigin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Additive discriminator on the retained legacy payments table (not a SmartnetDbContext entity, so
            // hand-written). Existing rows stay NULL — the pre-cutover legacy customer payments; the new
            // customer-receipt path writes 'new'. Lets the payments list show legacy history without
            // double-counting the new dual-writes.
            migrationBuilder.Sql(
                "ALTER TABLE `payments` ADD COLUMN `data_origin` varchar(16) NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE `payments` DROP COLUMN `data_origin`");
        }
    }
}
