-- ---------------------------------------------------------------------------
-- The database account a RESTORE runs as. Nothing else uses it.
-- ---------------------------------------------------------------------------
-- Run as an account with GRANT OPTION (root via sudo):
--
--     sudo mysql smartnet_invsys < backup-restore-user.sql
--
-- Then put the connection string in /var/www/sys-secrets/.env and restart the API:
--
--     Backup__RestoreConnectionString=Host=host.docker.internal;Database=smartnet_invsys;Username=smartnet_restore;Password=<the password>
--
-- Until that variable is set, backups and downloads work and the restore endpoints report themselves
-- unavailable. That is a supported state — a deployment that never wants a restore button reachable
-- from the web simply never sets it.
--
-- ---------------------------------------------------------------------------
-- READ THIS BEFORE RUNNING IT
-- ---------------------------------------------------------------------------
-- This account can destroy the business's records. It is deliberately NOT the application's user:
--
--   * The application user (smartnet_invsys_next) holds SELECT, INSERT and per-table UPDATE/DELETE
--     and NO DDL whatsoever. That is what narrow-app-user-grants.sh established, and it is what makes
--     `audit_log` genuinely append-only — the application cannot rewrite its own history.
--
--   * A restore must DROP and CREATE every table, `audit_log` included. So it needs an account that
--     can do the one thing the application was deliberately prevented from doing.
--
-- Consequences worth being clear-eyed about:
--
--   1. A restore overwrites the audit log. The record of who did what, including the record of the
--      restore itself, becomes whatever the backup contained. This is inherent to restoring a
--      database, not a flaw in the implementation, but it does mean a restore is the one action in
--      this system that can erase the evidence of itself. The application logs the restore at Warning
--      either side, to the container log, which the restore cannot reach.
--
--   2. Handing this credential to the API means a compromise of the API is a compromise of every
--      record. That trade was made deliberately so the restore button could exist in the UI. If that
--      ever stops feeling worth it, unset Backup__RestoreConnectionString: the button goes away and
--      the backups keep running.
--
-- Choose a real password. Generate one and do not reuse the application's:
--
--     openssl rand -base64 24
-- ---------------------------------------------------------------------------

-- CHANGE THIS before running.
CREATE USER IF NOT EXISTS 'smartnet_restore'@'%' IDENTIFIED BY 'CHANGE_ME';

-- Scoped to the one schema, and to what a restore actually does: recreate tables and refill them.
-- Not GRANT ALL — no GRANT OPTION, no privileges on any other database, nothing server-wide.
GRANT SELECT, INSERT, UPDATE, DELETE,
      CREATE, DROP, ALTER, INDEX, REFERENCES,
      CREATE VIEW, SHOW VIEW,
      CREATE ROUTINE, ALTER ROUTINE, EXECUTE,
      TRIGGER, LOCK TABLES
  ON `smartnet_invsys`.*
  TO 'smartnet_restore'@'%';

FLUSH PRIVILEGES;

-- Check what was granted. It should list exactly the schema above and USAGE on *.*, nothing more.
SHOW GRANTS FOR 'smartnet_restore'@'%';
