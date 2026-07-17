using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Smartnet.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase7SupplierInvPayOrigin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Additive discriminator on the retained legacy supplier_inv_pay table (not a SmartnetDbContext
            // entity, so hand-written). Existing rows stay NULL — they are the pre-cutover legacy payments;
            // the new supplier-payment path writes 'new'. Lets the supplier-payments list show legacy history
            // without double-counting the new dual-writes.
            migrationBuilder.Sql(
                "ALTER TABLE `supplier_inv_pay` ADD COLUMN `data_origin` varchar(16) NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE `supplier_inv_pay` DROP COLUMN `data_origin`");
        }
    }
}
