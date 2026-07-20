-- ---------------------------------------------------------------------------
-- Data exceptions — "Payment without an invoice".
--
-- 3 payment rows, 70,381.00, naming an invoice that exists nowhere:
--
--   id 349    3,006.00   2024-02-02   names nothing at all
--   id 473    1,000.00   2024-04-17   names STI-30
--   id 1209  66,375.00   2025-05-05   names SNI-1045
--
-- The business decision (2026-07-20) is to remove them: with no invoice behind
-- them there is nothing they can be applied to, and no company they can even be
-- attributed to.
--
-- READ THIS BEFORE RUNNING IT ON LIVE.
--
-- **These rows say money arrived.** Unlike the orphaned document lines, which
-- were counted by nothing, each of these is a record of a receipt. Deleting
-- them removes the only trace that 70,381.00 came in. That is why they are
-- archived first, and why this script is deliberately narrow: it removes only
-- rows naming an invoice number that matches NO invoice_h row at all.
--
-- Checked on smartnet_invsys_dev before this was written:
--   * 3 rows, 70,381.00 — matches the finding exactly.
--   * None would match if both sides were trimmed, so none is a whitespace
--     near-miss that could be re-attached to its invoice instead of deleted.
--   * Neither STI-30 nor SNI-1045 appears in del_invoice_h, so these are not
--     payments against deleted invoices — the numbers name nothing that ever
--     existed in that table.
--
-- **THE GL MUST GO WITH THEM.** All three carry a posted receipt — Dr Bank,
-- Cr Accounts Receivable — for their full amount. Deleting the payments alone
-- would strand 70,381.00 of Bank debits and Receivable credits in the trial
-- balance with no source document behind them, which turns a visible data
-- exception into an invisible accounting one. The lines and the entries are
-- removed with the payments, which is exactly what the app's own
-- RemoveDuplicatePayments correction does for the same reason.
--
-- No invoice balance is recomputed and no opening balance is reseeded: these
-- payments name no invoice, so there is no balance they were ever part of.
--
-- Reversible from the archives:
--   INSERT INTO payments   SELECT <cols> FROM archived_orphaned_payments;
--   INSERT INTO gl_entries SELECT <cols> FROM archived_orphaned_payment_gl_entries;
--   INSERT INTO gl_lines   SELECT <cols> FROM archived_orphaned_payment_gl_lines;
--
-- RE-RUNNABLE. Archiving skips what is already archived; the deletes are bounded
-- by the same rule, so a second run moves nothing.
--
--   mysql -h <host> -u <user> -p <database> < remove-orphaned-payments.sql
-- ---------------------------------------------------------------------------

-- --- Archive the payments ---------------------------------------------------

CREATE TABLE IF NOT EXISTS archived_orphaned_payments LIKE payments;
ALTER TABLE archived_orphaned_payments
  ADD COLUMN IF NOT EXISTS archived_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP;

INSERT INTO archived_orphaned_payments
SELECT p.*, NOW()
FROM payments p
WHERE (TRIM(COALESCE(p.invoiceno, '')) = ''
       OR NOT EXISTS (SELECT 1 FROM invoice_h h WHERE TRIM(h.invoiceno) = TRIM(p.invoiceno)))
  AND NOT EXISTS (SELECT 1 FROM archived_orphaned_payments a WHERE a.id = p.id);

-- --- Archive their GL postings ----------------------------------------------

CREATE TABLE IF NOT EXISTS archived_orphaned_payment_gl_entries LIKE gl_entries;
CREATE TABLE IF NOT EXISTS archived_orphaned_payment_gl_lines LIKE gl_lines;

INSERT INTO archived_orphaned_payment_gl_entries
SELECT e.* FROM gl_entries e
WHERE e.source_type = 'LegacyPayment'
  AND e.source_id IN (SELECT id FROM archived_orphaned_payments)
  AND NOT EXISTS (SELECT 1 FROM archived_orphaned_payment_gl_entries a WHERE a.id = e.id);

INSERT INTO archived_orphaned_payment_gl_lines
SELECT l.* FROM gl_lines l
WHERE l.gl_entry_id IN (SELECT id FROM archived_orphaned_payment_gl_entries)
  AND NOT EXISTS (SELECT 1 FROM archived_orphaned_payment_gl_lines a WHERE a.id = l.id);

-- --- Remove: lines, then entries, then the payments --------------------------

DELETE l FROM gl_lines l
WHERE l.gl_entry_id IN (
    SELECT id FROM gl_entries
    WHERE source_type = 'LegacyPayment'
      AND source_id IN (SELECT id FROM archived_orphaned_payments)
);

DELETE e FROM gl_entries e
WHERE e.source_type = 'LegacyPayment'
  AND e.source_id IN (SELECT id FROM archived_orphaned_payments);

DELETE p FROM payments p
WHERE TRIM(COALESCE(p.invoiceno, '')) = ''
   OR NOT EXISTS (SELECT 1 FROM invoice_h h WHERE TRIM(h.invoiceno) = TRIM(p.invoiceno));

-- --- Verify. Orphans 0, archives populated, and the GL still balances. -------

SELECT 'orphan payments remaining' AS check_, COUNT(*) AS n
FROM payments p
WHERE TRIM(COALESCE(p.invoiceno, '')) = ''
   OR NOT EXISTS (SELECT 1 FROM invoice_h h WHERE TRIM(h.invoiceno) = TRIM(p.invoiceno))
UNION ALL
SELECT 'archived payments', COUNT(*) FROM archived_orphaned_payments
UNION ALL
SELECT 'archived GL entries', COUNT(*) FROM archived_orphaned_payment_gl_entries
UNION ALL
SELECT 'archived GL lines', COUNT(*) FROM archived_orphaned_payment_gl_lines
UNION ALL
SELECT 'GL out of balance by (must be 0)',
       CAST(ROUND(SUM(debit) - SUM(credit), 2) AS SIGNED) FROM gl_lines;
