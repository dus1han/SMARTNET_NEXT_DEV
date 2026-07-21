-- ---------------------------------------------------------------------------
-- Creates `backup_settings` on live, and grants it to the application user.
-- ---------------------------------------------------------------------------
-- Run as an account with DDL and GRANT OPTION (root via sudo):
--
--     sudo mysql smartnet_invsys < apply-backup-settings.sql
--
-- WHY THIS IS A MANUAL STEP. The API does not migrate at startup — there is no Migrate() call in
-- Program.cs — so a release that adds a table does not create it. That is deliberate: the application's
-- own database user holds no DDL at all (narrow-app-user-grants.sh), which is what makes `audit_log`
-- genuinely append-only, and a process that can add a table can drop one.
--
-- Until this runs, the Backups screen returns an error and the hourly job logs one an hour. Nothing else
-- is affected: the rest of the application does not touch this table.
--
-- THE SECOND STATEMENT IS NOT OPTIONAL. The app user's UPDATE and DELETE are granted per table, so a
-- table created after that script ran has none. Without the grant the Backups screen would save once
-- (INSERT is schema-wide) and fail on every subsequent save with "access denied" — the kind of fault
-- that looks like the form is broken rather than like a missing privilege.
--
-- Re-runnable: the CREATE is guarded, and the history insert is idempotent.
-- ---------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS `backup_settings` (
    `id` bigint NOT NULL AUTO_INCREMENT,
    `enabled` tinyint(1) NOT NULL,
    `host` varchar(200) CHARACTER SET utf8mb4 NOT NULL,
    `port` int NOT NULL,
    `username` varchar(200) CHARACTER SET utf8mb4 NULL,
    `password_encrypted` varchar(1024) CHARACTER SET utf8mb4 NULL,
    `use_tls` tinyint(1) NOT NULL,
    `accept_any_certificate` tinyint(1) NOT NULL,
    `remote_path` varchar(255) CHARACTER SET utf8mb4 NOT NULL,
    `safety_path` varchar(255) CHARACTER SET utf8mb4 NOT NULL,
    `retention` int NOT NULL,
    `created_by` bigint NULL,
    `created_at` datetime(6) NOT NULL,
    `updated_by` bigint NULL,
    `updated_at` datetime(6) NULL,
    `deleted_by` bigint NULL,
    `deleted_at` datetime(6) NULL,
    `row_version` int NOT NULL,
    CONSTRAINT `PK_backup_settings` PRIMARY KEY (`id`)
) CHARACTER SET=utf8mb4;

-- So `dotnet ef` does not try to apply this migration again later.
INSERT IGNORE INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260721055546_Phase9BackupSettings', '9.0.0');

-- The application edits this row from the Backups screen. SELECT and INSERT are schema-wide; UPDATE and
-- DELETE are not, and a new table has neither until it is named.
GRANT UPDATE, DELETE ON `smartnet_invsys`.`backup_settings` TO 'smartnet_invsys_next'@'%';

FLUSH PRIVILEGES;

-- Should list the table, and the app user's grant on it.
SHOW TABLES LIKE 'backup_settings';
SHOW GRANTS FOR 'smartnet_invsys_next'@'%';
