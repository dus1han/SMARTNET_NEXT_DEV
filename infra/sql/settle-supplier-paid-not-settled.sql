-- ---------------------------------------------------------------------------
-- Data exceptions — "Supplier paid, not settled".
--
-- 38 supplier invoices carry paymentstat='Paid' with no row in supplier_inv_pay.
-- They are out of the payables outstanding (which reads paymentstat), but
-- nothing records who was paid or when — 1,435,253.01 of payables settled by
-- nothing.
--
-- The business decision (2026-07-20) is to record the missing settlement,
-- dated the invoice date.
--
-- READ THIS BEFORE RUNNING IT ON LIVE.
--
-- **The date is an assumption, not a recovered fact.** Nothing in the legacy
-- data records when these were actually paid; that is the whole defect. Dating
-- the settlement to the invoice date makes the record self-consistent and is
-- the most defensible date available — it is the one date we know relates to
-- the transaction — but it is not evidence that payment happened that day. If
-- the real dates exist on paper, entering those is strictly better than running
-- this.
--
-- Because of that, every row this writes is MARKED, two ways:
--   * referenceno = 'RECONSTRUCTED' — visible on the supplier invoice screen,
--     so anyone reading the record can see the settlement was reconstructed
--     rather than recorded at the time.
--   * data_origin = 'reconstructed' — machine-readable and permanent. Every
--     genuine legacy row has NULL here, so the two can always be told apart.
--
-- That marking is also what makes this reversible:
--
--   DELETE FROM supplier_inv_pay WHERE data_origin = 'reconstructed';
--
-- WHAT A SETTLEMENT MEANS HERE. A legacy supplier settlement carries no amount
-- of its own — supplier_inv_pay records which invoice was paid and when, and
-- the amount *is* the invoice's. So one row per invoice settles it in full,
-- which is exactly what paymentstat='Paid' already asserts. This adds the
-- missing record of it; it does not change what anybody is owed.
--
-- pay_method is NOT NULL and is left empty, matching the 1,617 existing rows
-- that carry no method.
--
-- RE-RUNNABLE. The insert skips any invoice that already has a settlement, so a
-- second run writes nothing.
--
--   mysql -h <host> -u <user> -p <database> < settle-supplier-paid-not-settled.sql
--
-- Note the CONVERT() on supinvid: it is utf8mb3_general_ci while supplier_invoice
-- is utf8mb3_unicode_ci, and comparing them directly is an illegal mix of
-- collations. That is a legacy schema quirk, not a data problem.
-- ---------------------------------------------------------------------------

INSERT INTO supplier_inv_pay (supinvid, paiddate, referenceno, pay_method, data_origin)
SELECT
    CAST(i.id AS CHAR),
    i.invdate,
    'RECONSTRUCTED',
    '',
    'reconstructed'
FROM supplier_invoice i
WHERE LOWER(TRIM(COALESCE(i.paymentstat, ''))) = 'paid'
  AND NOT EXISTS (
      SELECT 1 FROM supplier_inv_pay p
      WHERE CONVERT(p.supinvid USING utf8mb4) = CAST(i.id AS CHAR)
  );

-- --- Verify. "paid, not settled" must be 0; "settled twice" must be unchanged
-- --- at 1 (a separate defect this script deliberately does not touch).

SELECT 'paid, not settled remaining' AS check_, COUNT(*) AS n
FROM supplier_invoice i
WHERE LOWER(TRIM(COALESCE(i.paymentstat, ''))) = 'paid'
  AND NOT EXISTS (
      SELECT 1 FROM supplier_inv_pay p
      WHERE CONVERT(p.supinvid USING utf8mb4) = CAST(i.id AS CHAR)
  )
UNION ALL
SELECT 'settled more than once (unrelated defect)', COUNT(*) FROM (
    SELECT p.supinvid FROM supplier_inv_pay p GROUP BY p.supinvid HAVING COUNT(*) > 1
) x
UNION ALL
SELECT 'rows written by this script', COUNT(*)
FROM supplier_inv_pay WHERE data_origin = 'reconstructed';
