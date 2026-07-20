-- ---------------------------------------------------------------------------
-- Finding 3 / Data exceptions — "Lines without a document".
--
-- 690 line rows (608 invoice, 82 quotation) whose header does not exist. They
-- are counted by nothing and reachable by nothing, so they cost no money — but
-- every foreign key the new schema wants refuses to build while they stand,
-- which makes them a MIGRATION BLOCKER rather than an accounting one.
--
-- The business decision (2026-07-20) is to delete them: a line with no document
-- is of no use to anyone.
--
-- ARCHIVE FIRST, THEN DELETE. DATA-AUDIT-FINDINGS asked "delete or archive
-- first?" and this answers both — the rows are copied into archived_* tables
-- before they are removed, so the decision stays reversible and there is still
-- a record of what 4.6M of dangling line value actually consisted of. The
-- archive tables are not read by the application and can be dropped once the
-- migration is signed off.
--
-- WHAT COUNTS AS AN ORPHAN. Exactly what the Data Exceptions screen reports:
-- a line whose document reference is non-blank and matches no header. Verified
-- on smartnet_invsys_dev before this script was written:
--
--   * 608 + 82 — matches the finding exactly.
--   * Trimming the header side changes neither count, so none of these are
--     whitespace near-misses that could be re-attached instead of deleted.
--   * Every one has a NULL invoice_id / quotation_id, so none is attached to a
--     real document in the new schema — the dangling reference is legacy text
--     only. THIS IS THE CHECK THAT MATTERS: these tables were adopted and now
--     carry a real foreign key beside the legacy varchar, so a line could have
--     had a dead `inno` and a live `invoice_id`. None does.
--   * None has a header in del_invoice_h, so they are not the lines of deleted
--     invoices — deleting those would destroy the record of what was on them.
--   * No line has a blank reference, so the rule covers the whole table.
--
-- RE-RUNNABLE. Archiving skips rows already archived and the delete is bounded
-- by the same rule, so a second run moves nothing.
--
--   mysql -h <host> -u <user> -p <database> < remove-orphaned-lines.sql
--
-- RUN THE VERIFICATION AT THE BOTTOM. It must print zero on both tables.
-- Take a database backup first — this is a delete, and on live it is one of the
-- two changes that genuinely cannot be undone by re-running anything.
-- ---------------------------------------------------------------------------

-- --- Invoice lines ----------------------------------------------------------

CREATE TABLE IF NOT EXISTS archived_orphaned_invoice_l LIKE invoice_l;
ALTER TABLE archived_orphaned_invoice_l
  ADD COLUMN IF NOT EXISTS archived_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP;

INSERT INTO archived_orphaned_invoice_l
SELECT l.*, NOW()
FROM invoice_l l
WHERE TRIM(COALESCE(l.inno, '')) <> ''
  AND NOT EXISTS (SELECT 1 FROM invoice_h h WHERE TRIM(h.invoiceno) = TRIM(l.inno))
  AND NOT EXISTS (SELECT 1 FROM archived_orphaned_invoice_l a WHERE a.id = l.id);

DELETE l FROM invoice_l l
WHERE TRIM(COALESCE(l.inno, '')) <> ''
  AND NOT EXISTS (SELECT 1 FROM invoice_h h WHERE TRIM(h.invoiceno) = TRIM(l.inno));

-- --- Quotation lines --------------------------------------------------------

CREATE TABLE IF NOT EXISTS archived_orphaned_quotation_l LIKE quotation_l;
ALTER TABLE archived_orphaned_quotation_l
  ADD COLUMN IF NOT EXISTS archived_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP;

INSERT INTO archived_orphaned_quotation_l
SELECT l.*, NOW()
FROM quotation_l l
WHERE TRIM(COALESCE(l.qno, '')) <> ''
  AND NOT EXISTS (SELECT 1 FROM quotation_h h WHERE TRIM(h.q_no) = TRIM(l.qno))
  AND NOT EXISTS (SELECT 1 FROM archived_orphaned_quotation_l a WHERE a.id = l.id);

DELETE l FROM quotation_l l
WHERE TRIM(COALESCE(l.qno, '')) <> ''
  AND NOT EXISTS (SELECT 1 FROM quotation_h h WHERE TRIM(h.q_no) = TRIM(l.qno));

-- --- Verify. Both remaining counts must be 0. -------------------------------

SELECT 'invoice_l orphans remaining' AS check_, COUNT(*) AS n
FROM invoice_l l
WHERE TRIM(COALESCE(l.inno, '')) <> ''
  AND NOT EXISTS (SELECT 1 FROM invoice_h h WHERE TRIM(h.invoiceno) = TRIM(l.inno))
UNION ALL
SELECT 'quotation_l orphans remaining', COUNT(*)
FROM quotation_l l
WHERE TRIM(COALESCE(l.qno, '')) <> ''
  AND NOT EXISTS (SELECT 1 FROM quotation_h h WHERE TRIM(h.q_no) = TRIM(l.qno))
UNION ALL
SELECT 'archived invoice lines', COUNT(*) FROM archived_orphaned_invoice_l
UNION ALL
SELECT 'archived quotation lines', COUNT(*) FROM archived_orphaned_quotation_l;
