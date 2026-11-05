-- ---------------------------------------------------------------------------
-- Stock coming in records what one unit cost: adds stock_movements.unit_cost (nullable).
-- ---------------------------------------------------------------------------
-- Run as root via sudo, ON THE SERVER (see apply-proforma-invoices.sql for why it is manual):
--
--     scp infra/sql/apply-stock-receipt-cost.sql deploy@<host>:~/
--     ssh deploy@<host>
--     sudo mysql smartnet_invsys < ~/apply-stock-receipt-cost.sql
--
-- Migration 20261105074753_Phase10StockReceiptCost. Requires apply-suspended-receivables.sql first.
--
-- SAFE TO RUN AHEAD OF THE RELEASE, and should be: a new nullable column changes nothing for the running
-- version. The new version reads it on every item stock screen, so it must exist before the release.

START TRANSACTION;
ALTER TABLE `stock_movements` ADD `unit_cost` decimal(18,4) NULL;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261105074753_Phase10StockReceiptCost', '9.0.0');

COMMIT;


-- Should show unit_cost, Null = YES.
SHOW COLUMNS FROM `stock_movements` WHERE Field = 'unit_cost';
