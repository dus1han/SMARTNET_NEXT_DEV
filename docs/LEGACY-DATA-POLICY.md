# Legacy Data Policy

**Decision (2026-07-14): existing data is left as it is. Errors are prevented from here forward.**

No historical remediation. No recomputing of balances. No deletion of duplicate payments.
The 1.55M in duplicate payments, the 45 negative balances, the 4 broken totals and `STI-1068`
all remain exactly as they are.

This document defines how we honour that decision **without letting the errors propagate into
the new system.**

---

## 1. The core mechanism: a cutover line

Every record gets a marker:

```
data_origin  ENUM('legacy','new')   -- set once, never changed
legacy_locked TINYINT(1)            -- documents created before cutover
```

- **`legacy` records are read-only history.** They are displayed, printed and reported on
  exactly as they stand. They are never recalculated, never "corrected" by the new engine.
- **`new` records** are subject to every rule in the new system: `decimal` arithmetic,
  transactions, idempotency, foreign keys, per-line tax snapshots, the derived ledger.

The new system is therefore not claiming that history is correct — only that it is *faithfully
reproduced*.

---

## 2. Balances: opening balance, not recomputation

The new system derives balances from a ledger. Applying that to history would recompute the 45
broken balances and change your books — which is exactly what you asked not to do.

**So the ledger does not recompute history. It starts from it.**

For every existing invoice, one ledger entry is written at import:

```
ledger_entry {
  invoice_no:  'SNI-1162',
  type:        'OPENING_BALANCE',
  amount:      <the stored balance, verbatim — including if negative>,
  as_of:       <cutover date>,
  source:      'legacy-import',
  note:        'Imported from legacy system; not recalculated'
}
```

From that point on, every payment, credit note and adjustment is a new ledger entry, and the
balance is `opening_balance + sum(subsequent entries)`.

**Consequences, stated plainly:**
- The 45 negative balances remain negative. They will still show as negative.
- Accounts receivable remains understated by ~Rs. 1.55M until someone chooses to fix those
  45 invoices.
- Reports covering historical periods will continue to show the current (wrong) figures.
- **This is the accepted cost of the decision.** It is a business exposure, not a technical one.

**But:** from cutover forward, a duplicate payment cannot happen — transactions, an idempotency
key on payment submission, and a derived ledger make the mechanism that caused it impossible.

---

## 3. The one exception: orphaned rows must be archived

There is a single place where "leave it as it is" and "build it properly" genuinely conflict.

**608 orphaned invoice lines** (89 invoices whose headers no longer exist) will block any
foreign key we add between `invoice_l` and `invoice_h`. Without foreign keys, the new database
has no more integrity protection than the old one — and FKs are one of the main reasons the new
system won't repeat these errors.

**Proposal — archive, do not delete:**

```sql
CREATE TABLE _archive_orphan_invoice_l LIKE invoice_l;   -- + archived_at, archived_reason
INSERT INTO _archive_orphan_invoice_l SELECT ... ;        -- the 608 rows, preserved
DELETE FROM invoice_l WHERE inno NOT IN (SELECT invoiceno FROM invoice_h);
```

Why this does not violate the decision:
- These rows belong to invoices that **do not exist**. They appear on no document, in no
  report, in no balance, in no total. Nothing reads them.
- **No financial figure changes.** Not one balance, not one total, not one VAT return.
- Nothing is destroyed — every row is preserved in the archive table, queryable forever.

If you would still rather not touch them, the fallback is to add no foreign key on that
relationship. I do not recommend it: it means shipping the new system with the same structural
weakness that produced the mess. **Your call.**

---

## 4. Data Exceptions report — fix them when you're ready

Rather than a big remediation project, the new system ships a **Data Exceptions** screen that
lists every known defect, live:

- Invoices where the ledger disagrees with the stored legacy balance (the 45)
- Invoices marked paid with no payment record (`STI-1068`)
- Invoices whose lines don't sum to the header (the 4)
- Duplicate payment groups (the 44)

Each row shows the discrepancy and offers a **permission-gated correction** — which writes a
proper, audited, reasoned adjustment entry rather than silently editing history.

This means: no forced remediation now, but the list is visible, it does not quietly grow, and
your accountant can work through it whenever they choose. **Deferred, not forgotten.**

---

## 5. Retype at cutover (Phase 9) — verified safe

The audit validated **all 2,485 invoice headers and 12,598 lines: every money value and every
date parses cleanly.** So the conversion is a type change only, with no data loss and no value
changes:

- money `varchar(100)` → `DECIMAL(18,4)`
- dates `varchar(100)` → `DATE` / `DATETIME`
- add primary keys, add foreign keys (after §3), add unique index on document numbers

This does not alter a single figure. It stops the *next* one from being wrong.

---

## 6. What still cannot be deferred

Two items are **not** data remediation and are not covered by this decision:

1. **Rotate the database password and the SMTP password.** Both are published in source code.
2. **Reset the two user passwords** (`chanaka`, `showroom` — 4 characters, plaintext) and force
   a change at first login.

Leaving historical data alone is a business judgement. Leaving the front door open is not.

---

## 7. Summary

| Finding | Treatment |
|---|---|
| Rs. 1.55M duplicate payments (44 groups) | **Left as-is.** Imported as opening balance. Listed in Data Exceptions. |
| 45 negative balances | **Left as-is.** Will continue to display as negative. |
| STI-1068 — paid, no payment | **Left as-is.** Listed in Data Exceptions. |
| 4 invoices, lines ≠ header | **Left as-is.** Listed in Data Exceptions. |
| 608 orphaned invoice lines | **Archived** (not deleted) so foreign keys can be added. No financial impact. |
| `invoice_l_old` (11,659 rows) | Archived / dropped — confirm. |
| money & dates as `varchar` | Retyped at cutover. Verified safe; no value changes. |
| No PKs / FKs | Added at cutover. |
| 4-char plaintext passwords | **Reset immediately** — not deferrable. |
