using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Smartnet.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase7ContactPhoneFromCustomer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The company-level phone (cus_m.contactno) is no longer collected — a contact person's phone is
            // enough. Carry the existing value onto the customer's first document contact (which has no phone
            // yet), so it is not lost. The legacy cus_m.contactno column is left as-is for legacy readers.
            // Validated against production data: 221 customers, all with a document contact to receive it.
            migrationBuilder.Sql("""
                UPDATE `customer_contacts` cc
                JOIN (
                    SELECT `customer_id`, MIN(`id`) AS first_id
                    FROM `customer_contacts`
                    WHERE `contact_usage` = 'DocumentsAndNotifications'
                    GROUP BY `customer_id`
                ) f ON f.first_id = cc.`id`
                JOIN `cus_m` c ON c.`id` = cc.`customer_id`
                SET cc.`phone` = c.`contactno`
                WHERE (cc.`phone` IS NULL OR cc.`phone` = '')
                  AND c.`contactno` IS NOT NULL AND TRIM(c.`contactno`) <> ''
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
