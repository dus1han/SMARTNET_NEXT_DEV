using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Smartnet.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase7CustomerContactUsage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A contact's purpose replaces "primary": DocumentsAndNotifications (printed on sales documents +
            // notified) or NotificationsOnly (notified, never printed). See ContactUsage.
            migrationBuilder.DropColumn(
                name: "is_primary",
                table: "customer_contacts");

            migrationBuilder.AddColumn<string>(
                name: "contact_usage",
                table: "customer_contacts",
                type: "varchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "DocumentsAndNotifications")
                .Annotation("MySql:CharSet", "utf8mb4");

            // Backfill the structured contacts from the legacy ;-separated cus_m.contactp/email — the same
            // rule as CustomersController.BackfillContacts, now a migration point so it runs at deploy.
            // Idempotent (only customers with no contacts yet). A name pairs with an email only when the two
            // lists are the same length; otherwise names become document contacts and the surplus emails
            // become notification-only contacts (nothing guessed at). Validated against production data.
            migrationBuilder.Sql("""
                INSERT INTO `customer_contacts` (`customer_id`, `name`, `email`, `contact_usage`, `created_at`, `row_version`)
                WITH RECURSIVE nums (n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM nums WHERE n < 30),
                elig AS (
                    SELECT `id` AS customer_id, `contactp`, `email` AS emails,
                        CASE WHEN `contactp` IS NULL OR TRIM(`contactp`) = '' THEN 0
                             ELSE LENGTH(`contactp`) - LENGTH(REPLACE(`contactp`, ';', '')) + 1 END AS n_names,
                        CASE WHEN `email` IS NULL OR TRIM(`email`) = '' THEN 0
                             ELSE LENGTH(`email`) - LENGTH(REPLACE(`email`, ';', '')) + 1 END AS n_emails
                    FROM `cus_m`
                    WHERE NOT EXISTS (SELECT 1 FROM `customer_contacts` cc WHERE cc.`customer_id` = `cus_m`.`id`)
                      AND ((`contactp` IS NOT NULL AND TRIM(`contactp`) <> '') OR (`email` IS NOT NULL AND TRIM(`email`) <> ''))
                )
                SELECT customer_id,
                       NULLIF(TRIM(SUBSTRING_INDEX(SUBSTRING_INDEX(`contactp`, ';', n), ';', -1)), ''),
                       NULLIF(TRIM(SUBSTRING_INDEX(SUBSTRING_INDEX(emails, ';', n), ';', -1)), ''),
                       'DocumentsAndNotifications', NOW(6), 1
                  FROM elig JOIN nums ON n <= n_names WHERE n_names = n_emails AND n_names > 0
                UNION ALL
                SELECT customer_id,
                       NULLIF(TRIM(SUBSTRING_INDEX(SUBSTRING_INDEX(`contactp`, ';', n), ';', -1)), ''),
                       NULL, 'DocumentsAndNotifications', NOW(6), 1
                  FROM elig JOIN nums ON n <= n_names WHERE NOT (n_names = n_emails AND n_names > 0)
                UNION ALL
                SELECT customer_id,
                       NULL,
                       NULLIF(TRIM(SUBSTRING_INDEX(SUBSTRING_INDEX(emails, ';', n), ';', -1)), ''),
                       'NotificationsOnly', NOW(6), 1
                  FROM elig JOIN nums ON n <= n_emails WHERE NOT (n_names = n_emails AND n_names > 0)
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "contact_usage",
                table: "customer_contacts");

            migrationBuilder.AddColumn<bool>(
                name: "is_primary",
                table: "customer_contacts",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);
        }
    }
}
