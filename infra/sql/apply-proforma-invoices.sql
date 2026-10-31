-- ---------------------------------------------------------------------------
-- Creates the proforma invoice tables on live, seeds a proforma numbering series
-- for each company, and grants the application user what it needs on them.
-- ---------------------------------------------------------------------------
-- Run as an account with DDL and GRANT OPTION (root via sudo), ON THE SERVER:
--
--     scp infra/sql/apply-proforma-invoices.sql deploy@<host>:~/
--     ssh deploy@<host>
--     sudo mysql smartnet_invsys < ~/apply-proforma-invoices.sql
--
-- Same shape and same reasons as apply-document-drafts.sql: the API never migrates at startup, the app
-- user has no DDL, and there is no SDK on the server. This is the plain (non-idempotent) script for
-- migration 20261031202629_Phase10ProformaInvoices, every statement terminated.
--
-- APPLY IT BEFORE THE RELEASE, NOT AFTER. The new API reads proforma_invoices on the quotation screens
-- too (list, detail, convert, edit, void), so a release running against a database without the table
-- breaks quotations. Applying it early costs nothing — nothing reads it until the release is live.
--
-- NUMBERING. One PROFORMA series per company that already numbers quotations, each its own sequence
-- from 1. The prefix is the quotation prefix with "Q-" turned into "PI-" (STQ- -> STPI-, SNQ- -> SNPI-);
-- an administrator can change it under Settings -> Numbering. Re-running inserts no second series.
--
-- STRICT MODE IS NOT A CONCERN: two CREATEs with explicit types, one INSERT ... SELECT of literals and
-- existing values, and grants.

START TRANSACTION;
CREATE TABLE `proforma_invoices` (
    `id` bigint NOT NULL AUTO_INCREMENT,
    `number` varchar(100) CHARACTER SET utf8mb4 NOT NULL,
    `company_id` bigint NOT NULL,
    `quotation_id` bigint NOT NULL,
    `quotation_number` varchar(100) CHARACTER SET utf8mb4 NOT NULL,
    `customer_id` bigint NOT NULL,
    `proforma_date` date NOT NULL,
    `contact_person` varchar(100) CHARACTER SET utf8mb4 NULL,
    `validity` varchar(100) CHARACTER SET utf8mb4 NULL,
    `prepared_by` bigint NULL,
    `subtotal` decimal(18,4) NOT NULL,
    `discount_percent` decimal(18,4) NOT NULL,
    `discount_amount` decimal(18,4) NOT NULL,
    `net_total` decimal(18,4) NOT NULL,
    `tax_rate_id` bigint NULL,
    `tax_rate_percentage` decimal(18,4) NOT NULL,
    `tax_amount` decimal(18,4) NOT NULL,
    `total_amount` decimal(18,4) NOT NULL,
    `converted_to_invoice_id` bigint NULL,
    `converted_at` datetime(6) NULL,
    `converted_by` bigint NULL,
    `created_by` bigint NULL,
    `created_at` datetime(6) NOT NULL,
    `updated_by` bigint NULL,
    `updated_at` datetime(6) NULL,
    `deleted_by` bigint NULL,
    `deleted_at` datetime(6) NULL,
    `row_version` int NOT NULL,
    CONSTRAINT `PK_proforma_invoices` PRIMARY KEY (`id`)
) CHARACTER SET=utf8mb4;

CREATE TABLE `proforma_invoice_lines` (
    `id` bigint NOT NULL AUTO_INCREMENT,
    `proforma_invoice_id` bigint NOT NULL,
    `item_id` bigint NULL,
    `item_code` varchar(100) CHARACTER SET utf8mb4 NULL,
    `description` text CHARACTER SET utf8mb4 NULL,
    `quantity` decimal(18,4) NOT NULL,
    `unit_price` decimal(18,4) NOT NULL,
    `discount_percent` decimal(18,4) NOT NULL,
    `gross` decimal(18,4) NOT NULL,
    `net` decimal(18,4) NOT NULL,
    `cost` decimal(18,4) NULL,
    `created_by` bigint NULL,
    `created_at` datetime(6) NOT NULL,
    `updated_by` bigint NULL,
    `updated_at` datetime(6) NULL,
    `deleted_by` bigint NULL,
    `deleted_at` datetime(6) NULL,
    `row_version` int NOT NULL,
    CONSTRAINT `PK_proforma_invoice_lines` PRIMARY KEY (`id`),
    CONSTRAINT `FK_proforma_invoice_lines_proforma_invoices_proforma_invoice_id` FOREIGN KEY (`proforma_invoice_id`) REFERENCES `proforma_invoices` (`id`) ON DELETE RESTRICT
) CHARACTER SET=utf8mb4;

CREATE INDEX `IX_proforma_invoice_lines_proforma_invoice_id` ON `proforma_invoice_lines` (`proforma_invoice_id`);

CREATE UNIQUE INDEX `IX_proforma_invoices_company_id_number` ON `proforma_invoices` (`company_id`, `number`);

CREATE INDEX `IX_proforma_invoices_converted_to_invoice_id` ON `proforma_invoices` (`converted_to_invoice_id`);

CREATE INDEX `IX_proforma_invoices_quotation_id` ON `proforma_invoices` (`quotation_id`);

INSERT INTO document_series (company_id, doc_type, prefix, next_number, padding, created_at, row_version)
SELECT q.company_id,
       'PROFORMA',
       CASE WHEN q.prefix LIKE '%Q-' THEN CONCAT(LEFT(q.prefix, CHAR_LENGTH(q.prefix) - 2), 'PI-') ELSE 'PI-' END,
       1, 0, UTC_TIMESTAMP(6), 1
FROM document_series q
WHERE q.doc_type = 'QUOTATION'
  AND NOT EXISTS (SELECT 1 FROM document_series p
                  WHERE p.company_id = q.company_id AND p.doc_type = 'PROFORMA');

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261031202629_Phase10ProformaInvoices', '9.0.0');

COMMIT;

-- Converting and voiding update a proforma; nothing deletes one (voiding is soft). DELETE is granted to
-- match every other document table (narrow-app-user-grants.sh): SELECT and INSERT are schema-wide; UPDATE
-- and DELETE are per table, and a new table has neither until it is named.
GRANT UPDATE, DELETE ON `smartnet_invsys`.`proforma_invoices` TO 'smartnet_invsys_next'@'%';
GRANT UPDATE, DELETE ON `smartnet_invsys`.`proforma_invoice_lines` TO 'smartnet_invsys_next'@'%';

FLUSH PRIVILEGES;

-- Should list both tables, one PROFORMA series per company, and the grants.
SHOW TABLES LIKE 'proforma%';
SELECT company_id, doc_type, prefix, next_number FROM document_series WHERE doc_type IN ('QUOTATION', 'PROFORMA') ORDER BY company_id, doc_type;
SHOW GRANTS FOR 'smartnet_invsys_next'@'%';
