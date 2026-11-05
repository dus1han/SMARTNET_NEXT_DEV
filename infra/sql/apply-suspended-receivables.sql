-- ---------------------------------------------------------------------------
-- The suspended account: an invoice's outstanding moved off it, and brought back later.
-- Creates suspended_receivables and grants receivables.suspend to Dev_Admin and Company_Admin.
-- ---------------------------------------------------------------------------
-- Run as root via sudo, ON THE SERVER (see apply-proforma-invoices.sql for why it is manual):
--
--     scp infra/sql/apply-suspended-receivables.sql deploy@<host>:~/
--     ssh deploy@<host>
--     sudo mysql smartnet_invsys < ~/apply-suspended-receivables.sql
--
-- Migration 20261105072850_Phase10SuspendedReceivables. Requires apply-proforma-standalone.sql first.
--
-- SAFE TO RUN AHEAD OF THE RELEASE, and should be: a new table and a new grant change nothing for the
-- running version. The new version needs the table before any invoice is read, or invoice screens fail.

START TRANSACTION;
CREATE TABLE `suspended_receivables` (
    `id` bigint NOT NULL AUTO_INCREMENT,
    `company_id` bigint NOT NULL,
    `customer_id` bigint NOT NULL,
    `invoice_id` bigint NOT NULL,
    `invoice_number` varchar(100) CHARACTER SET utf8mb4 NOT NULL,
    `amount` decimal(18,4) NOT NULL,
    `suspended_on` date NOT NULL,
    `reason` varchar(500) CHARACTER SET utf8mb4 NOT NULL,
    `status` varchar(24) CHARACTER SET utf8mb4 NOT NULL,
    `reinstated_at` datetime(6) NULL,
    `reinstated_by` bigint NULL,
    `reinstate_note` varchar(500) CHARACTER SET utf8mb4 NULL,
    `receipt_id` bigint NULL,
    `created_by` bigint NULL,
    `created_at` datetime(6) NOT NULL,
    `updated_by` bigint NULL,
    `updated_at` datetime(6) NULL,
    `deleted_by` bigint NULL,
    `deleted_at` datetime(6) NULL,
    `row_version` int NOT NULL,
    CONSTRAINT `PK_suspended_receivables` PRIMARY KEY (`id`)
) CHARACTER SET=utf8mb4;

CREATE INDEX `IX_suspended_receivables_customer_id` ON `suspended_receivables` (`customer_id`);

CREATE INDEX `IX_suspended_receivables_invoice_id_status` ON `suspended_receivables` (`invoice_id`, `status`);

CREATE INDEX `IX_suspended_receivables_status_company_id` ON `suspended_receivables` (`status`, `company_id`);

INSERT INTO role_permissions (role_id, permission)
SELECT r.id, 'receivables.suspend'
FROM roles r
WHERE r.name IN ('Dev_Admin', 'Company_Admin')
  AND r.deleted_at IS NULL
  AND NOT EXISTS (
      SELECT 1 FROM role_permissions rp
      WHERE rp.role_id = r.id AND rp.permission = 'receivables.suspend');

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261105072850_Phase10SuspendedReceivables', '9.0.0');

COMMIT;




-- Should show the new table, and one receivables.suspend row per administrator role.
SHOW TABLES LIKE 'suspended_receivables';
SELECT r.name, rp.permission FROM role_permissions rp JOIN roles r ON r.id = rp.role_id WHERE rp.permission = 'receivables.suspend';
