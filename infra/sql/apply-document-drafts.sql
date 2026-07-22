-- ---------------------------------------------------------------------------
-- Creates `document_drafts` on live, and grants it to the application user.
-- ---------------------------------------------------------------------------
-- Run as an account with DDL and GRANT OPTION (root via sudo), ON THE SERVER:
--
--     scp infra/sql/apply-document-drafts.sql deploy@<host>:~/
--     ssh deploy@<host>
--     sudo mysql smartnet_invsys < ~/apply-document-drafts.sql
--
-- IT CANNOT BE RUN FROM A DEVELOPER MACHINE. `root` is localhost-only on the VPS — a remote attempt is
-- refused at authentication, not at privileges — and the application's own account has no grants on
-- `smartnet_invsys` at all, by design (DEVELOPMENT.md), so a mistyped connection string fails loudly
-- rather than damaging live data. Both were confirmed by trying, on 2026-07-22.
--
-- AND IT HAS TO BE COPIED, not pulled: there is no repository checkout on the VPS, and `dotnet` there
-- is the runtime only (v8, no SDK), so `dotnet ef database update` cannot be run on the server either.
-- See MIGRATION-DATA-CHECKS.md, "Applying the migrations to live".
--
-- STRICT MODE IS NOT A CONCERN HERE. Live runs sql_mode=STRICT_TRANS_TABLES, which is what aborted the
-- 2026-07-20 migration on a five-digit date. Nothing in this file converts or inserts data: it is a
-- CREATE with explicit types, an INSERT of two string literals, and a GRANT.
--
-- WHY THIS IS A MANUAL STEP. The API does not migrate at startup — there is no Migrate() call in
-- Program.cs — so a release that adds a table does not create it. That is deliberate: the application's
-- own database user holds no DDL at all (narrow-app-user-grants.sh), which is what makes `audit_log`
-- genuinely append-only, and a process that can add a table can drop one. The same reasoning, and the
-- same shape, as apply-backup-settings.sql.
--
-- WHAT IT IS FOR. Autosaved work on the four create screens — quotation, invoice, purchase order and
-- job card. A draft is not a document: it takes no number, posts nothing to the ledger, moves no stock,
-- and the legacy app cannot see it. See Smartnet.Domain/Documents/DocumentDraft.cs for why it is a table
-- of its own rather than a status column on quotation_h.
--
-- SAFE TO RUN AHEAD OF THE RELEASE. Nothing reads this table until the new API is deployed, so applying
-- it early costs nothing. Until it exists, the four create screens autosave nothing and every keystroke
-- lives only in the browser — the behaviour before this feature, plus an error in the log each time a
-- save is attempted.
--
-- NOTHING ELSE IN THIS RELEASE NEEDS THE DATABASE. The concurrent-edit work that ships alongside it only
-- reads `row_version`, which every audited table already has.
--
-- THE GRANT IS NOT OPTIONAL. The app user's UPDATE and DELETE are granted per table, so a table created
-- after narrow-app-user-grants.sh ran has neither. Without it a draft would be created once (INSERT is
-- schema-wide) and then fail on every autosave and every discard with "access denied" — which reads on
-- screen as the feature being broken rather than as a missing privilege.
--
-- CHECK THE USER NAME BELOW BEFORE RUNNING. It is the one apply-backup-settings.sql grants to. If live
-- runs the API as a different account, change both the GRANT and the verification at the foot.
--
-- Re-runnable: the CREATE is guarded, the history insert is idempotent, and GRANT is not cumulative.
-- ---------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS `document_drafts` (
    `id` bigint NOT NULL AUTO_INCREMENT,
    -- QUOTATION | INVOICE | PO | JOBCARD — the DocumentTypes constants.
    `doc_type` varchar(20) CHARACTER SET utf8mb4 NOT NULL,
    `company_id` bigint NOT NULL,
    -- The create screen's own state, as the browser serialised it. Opaque to the server: it is stored
    -- and handed back verbatim, checked only for being well-formed JSON. longtext because a 200-line
    -- invoice is past what fits in a row, and text rather than JSON because nothing queries inside it.
    `payload` longtext CHARACTER SET utf8mb4 NOT NULL,
    -- Denormalised from the payload by the browser, for the Drafts list.
    `party_name` varchar(200) CHARACTER SET utf8mb4 NULL,
    `total` decimal(18,2) NULL,
    `line_count` int NOT NULL,
    `created_by` bigint NULL,
    `created_at` datetime(6) NOT NULL,
    `updated_by` bigint NULL,
    -- NOT NULL, unlike the audit columns elsewhere: a draft is stamped by the controller rather than by
    -- the audit interceptor, and is always written with both timestamps set.
    `updated_at` datetime(6) NOT NULL,
    -- The concurrency token. Drafts are shared within a company, so an autosave carrying a stale version
    -- is refused rather than silently overwriting whoever saved first.
    `row_version` int NOT NULL,
    CONSTRAINT `PK_document_drafts` PRIMARY KEY (`id`),
    -- The only query the lists make: this company's drafts of one type, most recently touched first.
    -- Declared inside the CREATE rather than as a separate CREATE INDEX so that `IF NOT EXISTS` covers
    -- it too — a standalone CREATE INDEX has no portable guard, and re-running this file would fail on
    -- a duplicate key name after doing nothing else.
    KEY `IX_document_drafts_company_id_doc_type_updated_at` (`company_id`, `doc_type`, `updated_at`)
) CHARACTER SET=utf8mb4;

-- So `dotnet ef` does not try to apply this migration again later.
INSERT IGNORE INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260722075309_Phase9DocumentDrafts', '9.0.0');

-- Autosave rewrites the draft as the user types, and raising or discarding one removes it. SELECT and
-- INSERT are schema-wide; UPDATE and DELETE are not, and a new table has neither until it is named.
GRANT UPDATE, DELETE ON `smartnet_invsys`.`document_drafts` TO 'smartnet_invsys_next'@'%';

FLUSH PRIVILEGES;

-- Should list the table, the index, and the app user's grant on it.
SHOW TABLES LIKE 'document_drafts';
SHOW INDEX FROM `document_drafts`;
SHOW GRANTS FOR 'smartnet_invsys_next'@'%';
