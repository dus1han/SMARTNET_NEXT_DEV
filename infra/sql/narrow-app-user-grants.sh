#!/bin/sh
# ---------------------------------------------------------------------------
# Make audit_log and document_versions genuinely append-only for the app user.
#
# audit-log-grants.sql revokes UPDATE and DELETE on those two tables — and on
# its own that achieves NOTHING, which its closing note warns about. MySQL takes
# the UNION of privileges across levels, so a schema-wide
#
#     GRANT ALL PRIVILEGES ON `smartnet_invsys`.* TO 'smartnet_invsys_next'@'%'
#
# re-opens both tables no matter what is revoked at table level. That grant is
# exactly what the app user held, on dev and on live, which is why
# `DELETE FROM audit_log` succeeded as the application.
#
# So the schema-wide grant has to go. What replaces it:
#
#   * schema level  — SELECT, INSERT and the incidentals EF needs. Neither
#     SELECT nor INSERT can rewrite history, so granting them broadly is safe.
#   * table level   — UPDATE and DELETE on every table EXCEPT the two. The app
#     can still edit and remove business records; it cannot touch the log.
#
# Nothing here grants DDL. The API does not migrate at startup — no Migrate()
# call anywhere in Program.cs — so migrations stay an out-of-band step run under
# a different account, and the running application never needs CREATE or ALTER.
#
# Run as an account with GRANT OPTION (root via sudo):
#     sudo sh narrow-app-user-grants.sh [database]
#
# Re-runnable: it revokes and re-grants from scratch each time.
# ---------------------------------------------------------------------------
set -e

DB="${1:-smartnet_invsys}"
USER_SPEC="'smartnet_invsys_next'@'%'"
WORK=$(mktemp)

echo "Narrowing grants for ${USER_SPEC} on \`${DB}\`"

# --- 1. Drop the schema-wide grant, then re-grant only what cannot rewrite history.
{
  echo "REVOKE ALL PRIVILEGES ON \`${DB}\`.* FROM ${USER_SPEC};"
  echo "GRANT SELECT, INSERT, CREATE TEMPORARY TABLES, EXECUTE, LOCK TABLES, SHOW VIEW ON \`${DB}\`.* TO ${USER_SPEC};"
} > "${WORK}"

# --- 2. UPDATE and DELETE, per table, for everything except the append-only pair.
#
# The user spec is doubled-quoted here because it is being written INSIDE a SQL
# string literal: 'user'@'%' would close the literal on its first quote, which is
# how the first version of this script failed.
USER_SPEC_SQL="''smartnet_invsys_next''@''%''"

mysql -N -e "
  SELECT CONCAT('GRANT UPDATE, DELETE ON \`${DB}\`.\`', table_name, '\` TO ${USER_SPEC_SQL};')
  FROM information_schema.tables
  WHERE table_schema = '${DB}'
    AND table_type = 'BASE TABLE'
    AND table_name NOT IN ('audit_log', 'document_versions');
" >> "${WORK}"

echo "FLUSH PRIVILEGES;" >> "${WORK}"

echo "statements to apply: $(wc -l < "${WORK}")"
mysql < "${WORK}"
rm -f "${WORK}"

# --- 3. Show the result. audit_log and document_versions must appear with only
# --- SELECT, INSERT — and must NOT appear in any UPDATE/DELETE grant.
echo
echo "--- resulting grants ---"
mysql -N -e "SHOW GRANTS FOR ${USER_SPEC};" | sed -E 's/IDENTIFIED BY PASSWORD.*//'
