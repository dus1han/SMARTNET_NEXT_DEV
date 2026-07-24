-- ---------------------------------------------------------------------------
-- Creates `user_mail_accounts` on live, and grants it to the application user.
-- ---------------------------------------------------------------------------
-- Run as an account with DDL and GRANT OPTION (root via sudo), ON THE SERVER:
--
--     scp infra/sql/apply-user-mail-accounts.sql deploy@<host>:~/
--     ssh deploy@<host>
--     sudo mysql smartnet_invsys < ~/apply-user-mail-accounts.sql
--
-- Same reasoning and shape as apply-mail-accounts.sql: the API does not migrate at startup, and the
-- application's own account holds no DDL, so a release that adds a table needs a hand-run, idempotent
-- CREATE + history insert + the per-table UPDATE grant the narrow app-user grants don't cover. It cannot
-- be run from a developer machine — `root` is localhost-only on the VPS.
--
-- WHAT IT IS FOR. The "Assign mailboxes" action on Administration → Users. This join says which shared
-- mailboxes a user holds; a user can hold several and a mailbox can be shared by several. It depends on
-- `mail_accounts` already existing (apply-mail-accounts.sql) — the assignment points at a mailbox there.
--
-- SAFE TO RUN AHEAD OF THE RELEASE. Nothing reads this table until the new API is deployed; until it
-- exists, the users list and the assign dialog error and everything else is unaffected.
--
-- THE GRANT IS NOT OPTIONAL. Assigning is an INSERT (schema-wide, already held), but UN-assigning is a
-- soft-delete UPDATE and re-assigning is a restore UPDATE — and UPDATE is granted per table, so a table
-- created after narrow-app-user-grants.sh ran has none. Without it the first assign works and every change
-- after reads on screen as the feature being broken. (DELETE is granted alongside for symmetry with the
-- other tables; the app soft-deletes, so it is not strictly used.)
--
-- CHECK THE USER NAME BELOW BEFORE RUNNING. If live runs the API as a different account, change both the
-- GRANT and the verification at the foot.
--
-- Re-runnable: the CREATE is guarded, the history insert is idempotent, and GRANT is not cumulative.
-- ---------------------------------------------------------------------------

-- One row per (user, mailbox). Soft-deleted (a query filter hides deleted_at IS NOT NULL) and audited, so
-- who-held-what-when survives an unassign; the unique key is what a re-assign restores rather than doubles.
CREATE TABLE IF NOT EXISTS `user_mail_accounts` (
    `id` bigint NOT NULL AUTO_INCREMENT,
    `user_id` bigint NOT NULL,
    `mail_account_id` bigint NOT NULL,
    `created_by` bigint NULL,
    `created_at` datetime(6) NOT NULL,
    `updated_by` bigint NULL,
    `updated_at` datetime(6) NULL,
    `deleted_by` bigint NULL,
    `deleted_at` datetime(6) NULL,
    `row_version` int NOT NULL,
    CONSTRAINT `PK_user_mail_accounts` PRIMARY KEY (`id`),
    UNIQUE KEY `IX_user_mail_accounts_user_id_mail_account_id` (`user_id`, `mail_account_id`)
) CHARACTER SET=utf8mb4;

-- So `dotnet ef` does not try to apply this migration again later.
INSERT IGNORE INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260724120940_Phase9UserMailAccounts', '9.0.0');

-- Unassign is a soft-delete UPDATE and re-assign a restore UPDATE. SELECT and INSERT are schema-wide;
-- UPDATE and DELETE are not, and a new table has neither until it is named.
GRANT UPDATE, DELETE ON `smartnet_invsys`.`user_mail_accounts` TO 'smartnet_invsys_next'@'%';

FLUSH PRIVILEGES;

-- Should list the table, and the app user's grant on it.
SHOW TABLES LIKE 'user\_mail\_accounts';
SHOW GRANTS FOR 'smartnet_invsys_next'@'%';
