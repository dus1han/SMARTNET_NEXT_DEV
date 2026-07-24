-- ---------------------------------------------------------------------------
-- Creates `mail_server_settings` and `mail_accounts` on live, and grants them to
-- the application user.
-- ---------------------------------------------------------------------------
-- Run as an account with DDL and GRANT OPTION (root via sudo), ON THE SERVER:
--
--     scp infra/sql/apply-mail-accounts.sql deploy@<host>:~/
--     ssh deploy@<host>
--     sudo mysql smartnet_invsys < ~/apply-mail-accounts.sql
--
-- IT CANNOT BE RUN FROM A DEVELOPER MACHINE. `root` is localhost-only on the VPS — a remote attempt is
-- refused at authentication, not at privileges — and the application's own account has no DDL on
-- `smartnet_invsys` at all, by design (DEVELOPMENT.md). Same reasoning and shape as apply-document-drafts.sql.
--
-- WHY THIS IS A MANUAL STEP. The API does not migrate at startup — there is no Migrate() in Program.cs —
-- so a release that adds a table does not create it. The application's own user holds no DDL
-- (narrow-app-user-grants.sh), which is what keeps `audit_log` genuinely append-only; a process that can
-- add a table can drop one.
--
-- WHAT IT IS FOR. The Administration → Mail accounts and cPanel screens. `mail_server_settings` is one row:
-- the mail domain, the shared SMTP + IMAP/POP3 server every mailbox uses, and the cPanel connection whose
-- API token (encrypted) creates and re-passwords real mailboxes on the host. `mail_accounts` is the
-- mailboxes themselves. Nothing sends through them yet, so nothing else in this release needs the database.
--
-- SAFE TO RUN AHEAD OF THE RELEASE. Nothing reads these tables until the new API is deployed. Until they
-- exist, the two screens error with "table doesn't exist" and everything else is unaffected.
--
-- THE GRANTS ARE NOT OPTIONAL. The app user's UPDATE and DELETE are granted per table, so a table created
-- after narrow-app-user-grants.sh ran has neither. Without them: saving the server settings a second time,
-- and editing / disabling / removing a mailbox, would all fail with "access denied" — which reads on screen
-- as the feature being broken rather than as a missing privilege. (INSERT and SELECT are schema-wide, so the
-- first save and the listing would work, which is exactly what makes the omission look like a partial bug.)
--
-- CHECK THE USER NAME BELOW BEFORE RUNNING. It is the one apply-backup-settings.sql grants to. If live runs
-- the API as a different account, change both the GRANTs and the verification at the foot.
--
-- Re-runnable: the CREATEs are guarded, the history insert is idempotent, and GRANT is not cumulative.
-- ---------------------------------------------------------------------------

-- The shared server and the cPanel connection — one row. The API token is encrypted at rest (its column
-- ends in _encrypted, so the audit redaction covers it) and never returned by any endpoint.
CREATE TABLE IF NOT EXISTS `mail_server_settings` (
    `id` bigint NOT NULL AUTO_INCREMENT,
    -- The domain every mailbox lives on — "smart-net.lk". The add screen appends it; cPanel uses it as the
    -- mailbox domain.
    `mail_domain` varchar(200) CHARACTER SET utf8mb4 NULL,
    `outgoing_host` varchar(200) CHARACTER SET utf8mb4 NOT NULL,
    `outgoing_port` int NOT NULL,
    `outgoing_use_ssl` tinyint(1) NOT NULL,
    -- IMAP | POP3
    `incoming_protocol` varchar(8) CHARACTER SET utf8mb4 NOT NULL,
    `incoming_host` varchar(200) CHARACTER SET utf8mb4 NOT NULL,
    `incoming_port` int NOT NULL,
    `incoming_use_ssl` tinyint(1) NOT NULL,
    -- The cPanel connection. When host + username + token are all set, provisioning is active — no separate
    -- switch. The token is scoped to the cPanel account and can create, re-password and delete mailboxes.
    `cpanel_host` varchar(200) CHARACTER SET utf8mb4 NULL,
    `cpanel_port` int NOT NULL,
    `cpanel_username` varchar(100) CHARACTER SET utf8mb4 NULL,
    `cpanel_api_token_encrypted` varchar(1024) CHARACTER SET utf8mb4 NULL,
    `created_by` bigint NULL,
    `created_at` datetime(6) NOT NULL,
    `updated_by` bigint NULL,
    `updated_at` datetime(6) NULL,
    `deleted_by` bigint NULL,
    `deleted_at` datetime(6) NULL,
    `row_version` int NOT NULL,
    CONSTRAINT `PK_mail_server_settings` PRIMARY KEY (`id`)
) CHARACTER SET=utf8mb4;

-- One managed mailbox. Soft-deleted (a query filter hides deleted_at IS NOT NULL), audited, write-only
-- password.
CREATE TABLE IF NOT EXISTS `mail_accounts` (
    `id` bigint NOT NULL AUTO_INCREMENT,
    -- The sender/display name, and the list label.
    `display_name` varchar(100) CHARACTER SET utf8mb4 NOT NULL,
    -- The mailbox address — the login username and the from-address both.
    `email_address` varchar(200) CHARACTER SET utf8mb4 NOT NULL,
    `password_encrypted` varchar(1024) CHARACTER SET utf8mb4 NULL,
    `enabled` tinyint(1) NOT NULL,
    `created_by` bigint NULL,
    `created_at` datetime(6) NOT NULL,
    `updated_by` bigint NULL,
    `updated_at` datetime(6) NULL,
    `deleted_by` bigint NULL,
    `deleted_at` datetime(6) NULL,
    `row_version` int NOT NULL,
    CONSTRAINT `PK_mail_accounts` PRIMARY KEY (`id`)
) CHARACTER SET=utf8mb4;

-- So `dotnet ef` does not try to apply this migration again later.
INSERT IGNORE INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260724110549_Phase9MailAccounts', '9.0.0');

-- Saving the server row again is an UPDATE; editing, disabling (a soft-delete UPDATE) and removing a mailbox
-- are too. SELECT and INSERT are schema-wide; UPDATE and DELETE are not, and a new table has neither until
-- it is named.
GRANT UPDATE, DELETE ON `smartnet_invsys`.`mail_server_settings` TO 'smartnet_invsys_next'@'%';
GRANT UPDATE, DELETE ON `smartnet_invsys`.`mail_accounts` TO 'smartnet_invsys_next'@'%';

FLUSH PRIVILEGES;

-- Should list both tables, and the app user's grants on them.
SHOW TABLES LIKE 'mail\_%';
SHOW GRANTS FOR 'smartnet_invsys_next'@'%';
