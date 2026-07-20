-- ---------------------------------------------------------------------------
-- A non-VAT-registered company carries only a zero rate.
-- ---------------------------------------------------------------------------
-- Smart Technologies is flagged is_vat_registered = 0, yet the multi-company seed
-- gave every company a "VAT 18%" default (it seeded FROM companies_m, blind to the
-- flag). The tax engine already forces 0% for a non-VAT company, so ST's documents
-- were taxed correctly regardless — but the settings screen showed it an 18% rate
-- it never charges, and the data said one thing while the behaviour said another.
--
-- This makes the data honest: for every non-VAT-registered company, soft-delete any
-- rate that charges tax, and make the zero rate its default. Soft-delete, not DELETE,
-- so any document that referenced the row keeps its foreign key; the settings screen
-- and the engine both read `deleted_at IS NULL`, so a soft-deleted row simply
-- disappears from view.
--
-- Idempotent: re-running it changes nothing once a company is already zero-only.
-- Taxation is unaffected either way — this is a display-and-data fix, not a tax change.
--
-- On live (2026-07-21) this touches exactly Smart Technologies.
-- ---------------------------------------------------------------------------

-- 1. Retire any tax-charging rate a non-VAT company holds.
UPDATE tax_rates tr
JOIN companies_m c ON c.id = tr.company_id
SET tr.deleted_at = UTC_TIMESTAMP()
WHERE c.is_vat_registered = 0
  AND tr.deleted_at IS NULL
  AND tr.percentage > 0;

-- 2. Its zero rate is the default (the one thing a non-VAT company can be taxed at).
UPDATE tax_rates tr
JOIN companies_m c ON c.id = tr.company_id
SET tr.is_default = 1
WHERE c.is_vat_registered = 0
  AND tr.deleted_at IS NULL
  AND tr.percentage = 0;
