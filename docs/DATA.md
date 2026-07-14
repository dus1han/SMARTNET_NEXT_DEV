# Live Data Strategy

The existing `smartnet_invsys` MySQL database is **live production data** and stays the system
of record throughout. There is no big-bang data migration and no second database — the old app
and the new one read and write the *same* tables while modules cut over one at a time.

That constraint drives everything below.

---

## 1. Two non-negotiable rules

### Rule 1 — every schema change is additive and backward-compatible
The legacy app keeps writing to these tables for months. So, until it is decommissioned:

| Allowed | Forbidden |
|---|---|
| Add a **nullable** or **defaulted** column | Rename a column |
| Add a new table | Change a column's type |
| Add an index | Drop a column or table |
| Add a trigger that only writes elsewhere | Add a `NOT NULL` column with no default |
| Widen a column (e.g. `DECIMAL(12,2)` → `DECIMAL(18,4)`) | Narrow a column |
| Add a `CHECK`/FK **only after** the data is verified clean | Add a constraint that existing rows violate |

Every legacy write must still succeed after every migration. If a change cannot be made
additively, it waits for Phase 9.

**Corollary:** money columns cannot be retyped to `DECIMAL` while the old app is live — the
new code reads them *as* `decimal` and writes `decimal`; the retype itself happens at cutover.
Widening is safe and can happen earlier.

### Rule 2 — never develop against production
```bash
mysqldump --single-transaction --routines --triggers \
          -h <host> -u <user> -p smartnet_invsys > smartnet_prod.sql
```
Restore into a local/dev MySQL (Docker). All development, all EF Core scaffolding, every
migration and the entire data audit runs there first. Production is touched only by a
reviewed, rehearsed migration.

⚠️ The dump contains customer data. Treat it as confidential; keep it out of git
(`.gitignore` already excludes `*.sql`). For a shared dev environment, anonymise names,
emails and phone numbers.

---

## 2. Data quality audit — run before writing any code

The legacy app has **no foreign keys enforced in code, no transactions, no server-side
validation, and money in `double`**. So the data almost certainly contains problems that the
new system — with its constraints and correct arithmetic — will surface loudly.

**Better to find them now than at cutover.** Each check below produces a count and a sample.

### 2.1 Money & arithmetic
- [ ] **Column types** — are `totamount`, `balance`, `amount`, `novattotal` etc. `DECIMAL`,
      or `DOUBLE`/`FLOAT`/`VARCHAR`? If they are floating or textual, every downstream figure
      is suspect.
- [ ] **Non-numeric values in money columns** (if any are stored as text).
- [ ] **Totals that don't reconcile:** for each invoice, does
      `SUM(lines.total)` + VAT − discount = `invoice_h.totamount`? Expect a tail of mismatches
      of a few cents — the `double` arithmetic. Quantify it.
- [ ] **Balances that don't reconcile:** does
      `invoice_h.balance` = `totamount` − `SUM(payments.amount)` − `SUM(credit notes)`?
      This is the one that matters most — balances are mutated in place today with no ledger.
- [ ] **Negative or impossible values:** negative quantities, negative balances, balances
      greater than the invoice total, zero-value invoices.
- [ ] **Rounding drift:** sum of all invoice totals vs sum of all line totals, per month.

### 2.2 Referential integrity (no FKs exist today)
- [ ] Invoice lines whose `invoiceno` has no header.
- [ ] Payments against an invoice number that doesn't exist.
- [ ] Credit notes referencing a missing invoice.
- [ ] Invoices whose `customer` code isn't in `cus_m`.
- [ ] Supplier invoices / POs referencing missing suppliers.
- [ ] Job cards, cheques, expenses with dangling references.
- [ ] Stock rows referencing missing items.

### 2.3 Identity & duplicates
- [ ] **Duplicate document numbers** — invoice, quotation, CN, PO. Numbering is not
      concurrency-safe today (ISSUES B4), so duplicates are *plausible*, not hypothetical.
- [ ] Duplicate customer codes, item codes, usernames.
- [ ] Customers/items differing only by whitespace or case.

### 2.4 Users, permissions & attribution
- [ ] `user_m` rows whose password is empty, or still `1234`.
- [ ] Users with **no** row in `user_permissions` (the login code does `temp[0]` — a user
      with no permission row makes the app throw).
- [ ] Distinct values of `addedby` / `enteredby` / `preparedby` — these are **display-name
      strings**, and must be mapped to user ids for the audit backfill. Expect names that no
      longer match any user, typos, and blanks.
- [ ] `utype` and `cuscode` distinct values — customer-portal accounts are **not being
      migrated**, so confirm which users are staff and which (if any) are customer logins to
      be disabled.

### 2.5 Dates & encoding
- [ ] Zero dates (`0000-00-00`) — MySQL permits them; .NET will reject them.
- [ ] Dates in the future, or before the business existed.
- [ ] Text stored with the wrong collation (mojibake), especially in names/addresses.
- [ ] `NULL` vs empty-string inconsistency in the same column.

### 2.6 Companies (multi-company)
- [ ] Distinct values of `invoice_h.company` (and the equivalent on other documents) —
      this is what maps to the new `companies` table. Confirm the SN/ST split is clean and
      that no document has a blank or unknown company.
- [ ] VAT presence vs company: SN documents should carry VAT, ST should not (per the report
      analysis). Any document contradicting that needs a decision.

**Deliverable:** a data-audit report — counts, samples, and a decision per finding
(fix in place / accept and record / block cutover).

---

## 3. Backfill — giving live data an audit trail it never had

The new system requires audit columns, versions and `company_id` on everything
([AUDIT.md](AUDIT.md)). Historical rows have none. Plan:

| Target | Backfill |
|---|---|
| `created_at` | From the best existing timestamp (`cdatetime`, `entereddt`, `indate`, `addeddate`). If none, `NULL` — do **not** invent a date. |
| `created_by` | Map the `addedby` / `preparedby` **name string** to a user id via a reviewed mapping table. Unmatched → a `SYSTEM (legacy)` user id, never a guess. |
| `updated_by/at` | `NULL` — the legacy app never recorded updates. Be honest about that; don't fabricate. |
| `company_id` | From the existing `company` column on each document. |
| `document_versions` | Write a **v1 snapshot** for every existing document, marked `source: 'legacy-import'`, `changed_at` = created date, `reason` = "Imported from legacy system". |
| `row_version` | Default `1`. |
| Tax rate per line | Backfill from the document's own stored VAT figures where present; where a line's rate cannot be determined, record it as *unknown* rather than assuming 5%. |

**Principle: never fabricate history.** A `NULL` that says "we don't know who did this" is
worth more than a plausible-looking lie in an audit table. Every backfilled row is flagged
`source = 'legacy-import'` so it is always distinguishable from data the new system captured.

---

## 4. Running both apps on one database

During the strangler period:

- **Passwords** — legacy writes plaintext, new writes Argon2 to `password_hash` and upgrades
  on login. Both columns coexist. Plaintext dropped in Phase 9.
- **Numbering** — while *both* apps can create documents, they must not both allocate numbers.
  Once a document type is ported, that type is **created only in the new app**; the legacy
  screen is removed from its menu. `document_series.next_number` is seeded from
  `MAX(existing number)` per company per type at cutover of that module.
- **Audit** — the legacy app writes without audit rows. Those writes are detectable (no
  matching `audit_log` entry) and are reported, not silently accepted. This also gives a
  clean signal that a module has *actually* stopped being used.
- **Balances** — the ledger is derived and reconciled continuously (below). Until invoices
  are ported, the legacy app is still mutating `balance` in place; the new ledger runs
  alongside and any divergence is an alarm.

---

## 5. Continuous reconciliation

From Phase 1, a nightly job compares old and new and reports differences:

- Sum of invoice totals per month — legacy vs new calculation.
- Customer balance per customer — stored `balance` vs the derived ledger.
- Document counts per type per month.
- VAT totals per period — this is the one the tax authority cares about.

Any divergence is investigated **before** it becomes a cutover surprise. Expect small
differences from `double` → `decimal`; the point is that they are *known and quantified*, not
discovered later.

---

## 6. Cutover checks (Phase 9)

- [ ] Reconciliation report is clean, or every difference is explained and accepted in writing.
- [ ] No legacy writes observed for 7 consecutive days (via the audit gap signal).
- [ ] Full backup taken and **restore rehearsed** — an untested backup is not a backup.
- [ ] Money columns retyped to `DECIMAL(18,4)`; foreign keys added; plaintext `password`
      column dropped. All in one reviewed migration, in a maintenance window, with a
      tested rollback.
- [ ] Legacy app archived, not deleted.

---

## 7. What I need to proceed

1. A **`mysqldump` of the current database** (schema + data) restored into a dev MySQL — or,
   at minimum, `mysqldump --no-data` so I can write the audit queries against the real schema.
2. Confirmation of which company codes exist in the data (expected: SN, ST).
3. Whether any **customer-portal logins** are live in `user_m` (they are not being migrated
   and will need disabling).

With the schema I can write the exact data-audit SQL — every check in §2 as a runnable query —
and we will know what the live data actually looks like before a line of application code
depends on it.
