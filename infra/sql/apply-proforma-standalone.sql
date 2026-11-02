-- ---------------------------------------------------------------------------
-- Lets a proforma invoice exist without a quotation: quotation_id and quotation_number become nullable.
-- ---------------------------------------------------------------------------
-- Run as root via sudo, ON THE SERVER (see apply-proforma-invoices.sql for why it is manual):
--
--     scp infra/sql/apply-proforma-standalone.sql deploy@<host>:~/
--     ssh deploy@<host>
--     sudo mysql smartnet_invsys < ~/apply-proforma-standalone.sql
--
-- Migration 20261102043250_Phase10ProformaStandalone. Requires apply-proforma-invoices.sql first.
--
-- SAFE TO RUN AHEAD OF THE RELEASE, and should be: relaxing NOT NULL changes nothing for the running
-- version, which always fills both columns. The new version needs it before anyone saves a proforma
-- entered on its own, or that save fails.

START TRANSACTION;
ALTER TABLE `proforma_invoices` MODIFY COLUMN `quotation_number` varchar(100) CHARACTER SET utf8mb4 NULL;

ALTER TABLE `proforma_invoices` MODIFY COLUMN `quotation_id` bigint NULL;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261102043250_Phase10ProformaStandalone', '9.0.0');

COMMIT;


-- Should show both columns as nullable (Null = YES).
SHOW COLUMNS FROM `proforma_invoices` WHERE Field IN ('quotation_id', 'quotation_number');
