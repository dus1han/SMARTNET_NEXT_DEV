-- ---------------------------------------------------------------------------
-- audit_log is append-only. This is what makes that true.
--
-- An append-only log that the application can rewrite is not evidence. So the
-- application's own database user is granted INSERT and SELECT on audit_log and
-- document_versions, and is granted neither UPDATE nor DELETE. A bug, or a
-- developer in a hurry, or an attacker holding the app's credentials, cannot
-- quietly amend the record of what they did.
--
-- This is enforced by the grant, not by a code review.
--
-- Run this ONCE per environment, as an admin user, AFTER the audit migration has
-- created the tables. It is not part of the EF migration: EF runs as the very
-- user whose privileges are being restricted, so it cannot be trusted to be the
-- thing that restricts them.
--
--   mysql -h <host> -u <admin> -p smartnet_invsys_dev < audit-log-grants.sql
--
-- Set @app_user / @app_host to match the application's credentials.
-- ---------------------------------------------------------------------------

SET @app_user := 'smartnet_invsys_next';
SET @app_host := '%';
SET @db       := DATABASE();

-- --- audit_log: insert and read. Never amend, never erase. --------------------
SET @sql := CONCAT('GRANT INSERT, SELECT ON `', @db, '`.`audit_log` TO ''', @app_user, '''@''', @app_host, '''');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @sql := CONCAT('REVOKE UPDATE, DELETE ON `', @db, '`.`audit_log` FROM ''', @app_user, '''@''', @app_host, '''');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- --- document_versions: same reasoning. A snapshot you can edit is not a snapshot.
SET @sql := CONCAT('GRANT INSERT, SELECT ON `', @db, '`.`document_versions` TO ''', @app_user, '''@''', @app_host, '''');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @sql := CONCAT('REVOKE UPDATE, DELETE ON `', @db, '`.`document_versions` FROM ''', @app_user, '''@''', @app_host, '''');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

FLUSH PRIVILEGES;

-- Verify. Expect INSERT and SELECT on both tables, and no UPDATE or DELETE.
SET @sql := CONCAT('SHOW GRANTS FOR ''', @app_user, '''@''', @app_host, '''');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- ---------------------------------------------------------------------------
-- NOTE — a database-wide GRANT ALL on the schema outranks the REVOKE above:
-- MySQL takes the union of the grants at every level, so a schema-level UPDATE
-- privilege re-opens the table regardless of what is revoked here. If the app
-- user currently holds schema-wide privileges, they must be narrowed to the
-- specific tables it needs. Check the output above before believing this worked.
-- ---------------------------------------------------------------------------
