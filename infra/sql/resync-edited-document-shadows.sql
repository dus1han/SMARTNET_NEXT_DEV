-- Re-syncs the legacy date shadows on documents this app edited.
--
-- WHY
-- ---
-- quotation_h.qdate and invoice_h.indate are the legacy varchar dates. The list screens order AND
-- display from them (QuotationsController.List, InvoicesController.List), while an edit wrote only the
-- typed quotation_date / invoice_date. So a document whose date was edited showed the new date on its
-- own screen and the old one in the list — and sorted by the old one.
--
-- The editors now refresh both. This repairs the rows written before they did.
--
-- SCOPE — read before running
-- ---------------------------
-- data_origin = 'new' ONLY, deliberately. Legacy rows have their own, unrelated mismatch: several
-- hundred carry dates the legacy app allowed and no calendar does ('0024-06-07', '0205-04-23'), which
-- the adoption backfilled to 1970-01-01. Those are a migration data question (MIGRATION-DATA-CHECKS.md),
-- not this bug, and overwriting qdate from the epoch value would destroy the only surviving record of
-- what was originally typed. Left strictly alone.
--
-- Idempotent: the WHERE clause matches nothing once the rows agree. Safe to re-run.
--
-- At the time of writing this affected exactly one row (STQ-223) and no invoices. It is written for both
-- tables anyway — the invoice editor had the identical defect and simply had not been exercised yet.

UPDATE quotation_h
   SET qdate = DATE_FORMAT(quotation_date, '%Y-%m-%d')
 WHERE data_origin = 'new'
   AND quotation_date IS NOT NULL
   AND qdate <> DATE_FORMAT(quotation_date, '%Y-%m-%d');

UPDATE invoice_h
   SET indate = DATE_FORMAT(invoice_date, '%Y-%m-%d')
 WHERE data_origin = 'new'
   AND invoice_date IS NOT NULL
   AND indate <> DATE_FORMAT(invoice_date, '%Y-%m-%d');

-- Verification. Both counts must be zero afterwards.
SELECT CONCAT('quotations still out of step: ', COUNT(*)) AS result
  FROM quotation_h
 WHERE data_origin = 'new' AND quotation_date IS NOT NULL
   AND qdate <> DATE_FORMAT(quotation_date, '%Y-%m-%d')
UNION ALL
SELECT CONCAT('invoices still out of step:   ', COUNT(*))
  FROM invoice_h
 WHERE data_origin = 'new' AND invoice_date IS NOT NULL
   AND indate <> DATE_FORMAT(invoice_date, '%Y-%m-%d');
