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
            // enough. Carry the existing value onto the customer's first *live* document contact, so it is not
            // lost. The legacy cus_m.contactno column is left as-is for legacy readers. Only fills where no
            // live contact already carries a phone, and targets a non-deleted contact (a contact edited before
            // this runs may have soft-deleted the original) — all derived tables, so MySQL allows the self-join.
            // Validated against production data.
            migrationBuilder.Sql("""
                UPDATE `customer_contacts` cc
                JOIN (
                    SELECT f.first_id
                    FROM (
                        SELECT `customer_id`, MIN(`id`) AS first_id
                        FROM `customer_contacts`
                        WHERE `contact_usage` = 'DocumentsAndNotifications' AND `deleted_at` IS NULL
                        GROUP BY `customer_id`
                    ) f
                    JOIN `cus_m` c ON c.`id` = f.`customer_id`
                    LEFT JOIN (
                        SELECT DISTINCT `customer_id` FROM `customer_contacts`
                        WHERE `deleted_at` IS NULL AND `phone` IS NOT NULL AND `phone` <> ''
                    ) hasphone ON hasphone.`customer_id` = f.`customer_id`
                    WHERE c.`contactno` IS NOT NULL AND TRIM(c.`contactno`) <> '' AND hasphone.`customer_id` IS NULL
                ) t ON t.first_id = cc.`id`
                JOIN `cus_m` c2 ON c2.`id` = cc.`customer_id`
                SET cc.`phone` = c2.`contactno`
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
