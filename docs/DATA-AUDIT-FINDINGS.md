# Data Audit — Findings

Run 2026-07-14 against `smartnet_invsys_dev` (a copy of live production) — read-only.
Server: MariaDB 10.11. 49 tables, ~2,485 invoices, 12,598 invoice lines, 2,275 payments.

> **Re-verified 2026-07-18.** Every finding below now has a live detection rule behind it in
> Reports → Data exceptions — Findings 3, 4 and 9 did not before, and Finding 9 claimed it did.
> Findings 12–15 were added by that re-verification. For what to do about each one at cutover, see
> [MIGRATION-DATA-CHECKS.md](MIGRATION-DATA-CHECKS.md).

---

## 🔴 FINDING 1 — Rs. 1,575,374 of duplicate payment records

**44 duplicate payment groups; 49 extra payment rows.**
Same invoice, same amount, same date, recorded two or more times.

| Check | Result |
|---|---|
| Duplicate payment groups | **44** |
| Extra (duplicate) rows | **49** |
| Overstated payment value | **Rs. 1,575,374** |
| Invoices now showing a negative balance | **45**, totalling **−Rs. 1,554,424** |

The two figures match, which confirms the mechanism. Examples:

| Invoice | Total | Recorded as paid | Multiple |
|---|---|---|---|
| STI-38 | 71,000 | 142,000 | ×2 |
| SNI-915 | 23,600 | 47,200 | ×2 |
| STI-1022 | 4,500 | 9,000 | ×2 |
| SNI-1162 | 14,750 | 88,500 | ×6 |

**Cause:** `CusPaymentsController.savePay` inserts the payment and then runs
`UPDATE invoice_h SET balance = balance - amount` — with **no transaction, no idempotency key
and no duplicate check**. A double-clicked Save button, or a retried request, records the
payment twice and subtracts it from the balance twice.

**Impact:** accounts receivable is **understated by ~Rs. 1.55M**. Customers who still owe money
appear to have overpaid. Statements and the outstanding report sent to customers are wrong.

**This is not a migration problem — it is wrong today, in production.** It needs an accounting
decision, not just a code fix.

> **✅ REMEDIATED (2026-07-17).** The business chose the direct cleanup. Migration
> `Phase8RemediateDuplicatePayments` deletes the 49 duplicate rows, recomputes the affected balances
> (45 negatives → 0), and clears the matching GL / receivables entries. **SI-35** — a duplicate that
> masked a real Rs. 50,000 receivable rather than a negative — was **confirmed owed by the business** and
> remediated to a 50,000 balance. Applied and verified on the dev copy; **must still be run on live** (with
> a pre-run snapshot). Full record and cutover steps in [remediation/RECONCILIATION.md](remediation/RECONCILIATION.md).
>
> **⚠️ Incomplete — see [Finding 12](#-finding-12--two-of-finding-1s-duplicates-survived-its-remediation).**
> The remediation matched on same invoice + same amount + **same date**. The example table above was
> built by comparing recorded payments against the invoice total, which is a wider test. **STI-38 and
> SNI-915 — two of the four examples listed above — are dated weeks apart, matched neither the rule nor
> the cleanup, and are still overpaid today.** Re-check with the Overpaid rule after running this on
> live; do not treat the migration's "49 rows deleted" as meaning no invoice is overpaid.

---

## 🔴 FINDING 2 — An invoice marked paid with no payment behind it

`STI-1068` (2026-03-25) — total **Rs. 21,500**, balance **0**, and **no payment row exists**.

Either the money was received and never recorded, or the balance was zeroed in error. Either
way it is a receivable that nobody is chasing because the system says it is settled.

---

## 🔴 FINDING 3 — 608 orphaned invoice lines across 89 invoices

608 rows in `invoice_l` whose `inno` matches no invoice header — and **none of the 89 invoice
numbers appear in `del_invoice_h`** either, so they were not soft-deleted through the app.
Headers were removed and their lines left behind.

Worst offenders: `STI-1159` (41 lines), `STI-1160` (40), `STI-833` (39), `SNI-454` (33).

These lines are counted by nothing and reachable by nothing, but they will break any
foreign-key constraint the new system tries to add.

---

## 🟠 FINDING 4 — 4 invoices whose lines don't sum to the header total

| Invoice | Header total | Sum of lines | Difference |
|---|---|---|---|
| STI-1150 | 12,041 | 1,916 | **10,125** |
| STI-869 | 22,535 | 21,835 | 700 |
| STI-60 | 3,405 | 3,165 | 240 |
| STI-717 | 246,015 | 245,955 | 60 |

`STI-1150` is not rounding drift — the header claims six times the value of its lines. Lines
were almost certainly deleted after the invoice was issued, with the header never recalculated.

---

## 🔴 FINDING 5 — Every monetary value in the database is stored as `varchar(100)`

Not `DECIMAL`. Not even `DOUBLE`. **Text.**

`invoice_h.totamount`, `balance`, `cost`, `novattotal`, `beforedisctot`, `discountper`,
`invoice_l.qty`, `rate`, `tot`, `payments.amount`, `cheques.amount`, `expense_tr.expense_amount`
— all `varchar`.

So the round trip for every figure in the system is:
**text in the database → parsed to `double` in C# → arithmetic in binary floating point →
formatted back to text.**

The database cannot enforce that a price is a number, cannot sum a column without casting, and
cannot prevent `"12,500"` or `"abc"` being written into an amount field.

**Mitigating good news:** I checked all 2,485 invoice headers and 12,598 lines — **every value
currently parses as numeric**. No junk has actually landed in a money column. So the retype to
`DECIMAL(18,4)` is safe to perform.

---

## 🔴 FINDING 6 — Three primary keys in a 49-table database. Zero foreign keys.

`invoice_h` has **no primary key** — no `id` column at all. Rows are identified by
`invoiceno`, a `varchar` with no unique index on it.

Nothing prevents duplicate invoice numbers, orphaned children, or a stray `UPDATE` hitting more
rows than intended. (Duplicate invoice numbers were checked: currently **zero** — you have been
lucky, not protected.)

Dates are `varchar` too (`indate`, `cdatetime`, `paymentrecdate`). All 2,485 parse correctly and
run 2023-01-09 → 2026-07-14, so the retype to `DATE`/`DATETIME` is also safe.

---

## 🔴 FINDING 7 — Two users, both with 4-character plaintext passwords

| id | username | name | type | status | password length |
|---|---|---|---|---|---|
| 1 | chanaka | Chanaka Kotugoda | Administrator | Active | **4** |
| 2 | showroom | Showroom | User | Active | **4** |

Four characters, stored in plaintext. Almost certainly still the hardcoded default `1234` that
`ManageUserController` assigns to every new user.

**The entire system — every invoice, every customer, all financial history — is protected by two
four-character passwords, stored in clear text, in a database whose credentials are published in
source code, on a port answering to the open internet.**

*(Also: no customer-portal logins exist. Confirms the decision to drop that module.)*

---

## 🟡 FINDING 8 — Configuration and leftovers

- **VAT is 18%**, effective 2024-01-01 → 9999-12-31 (`vat_validity`). A single rate; the second
  tax type is `NON-VAT` with a null rate. Confirms the SN (VAT) / ST (non-VAT) split.
- `companies_m` already exists with exactly two rows — **Smart Net** (vatcode 1 = VAT) and
  **Smart Technologies** (vatcode 2 = NON-VAT). The multi-company model maps cleanly onto it.
- **`invoice_l_old` — 11,659 rows.** A hand-made backup table sitting in the production
  schema. Confirm and drop.
- **`docstore` is 10.5 MB for 18 rows** — PDFs as BLOBs, as expected. It will not scale.
- Numbering lives in per-company sequence tables (`invoice_seq`, `invoice_seq_st`,
  `quotation_seq`, …) — these seed `document_series` cleanly.
- The web-catalogue tables (`wb_products`, `wb_prod_cat`, `wb_projects`) are present but small
  and unused. Dropped per decision.

---

## What needs a decision from you

| # | Decision | Who |
|---|---|---|
| 1 | **The Rs. 1.55M of duplicate payments** — do we delete the 49 duplicate rows and recompute balances, or is the accounting treatment more complicated? | You + your accountant |
| 2 | `STI-1068` — Rs. 21,500 marked paid with no payment. Was it received? | You |
| 3 | `STI-1150` and the other 3 broken totals — reissue, credit-note, or accept? | You + accountant |
| 4 | The 608 orphaned lines — delete, or archive first? | You |
| 5 | `invoice_l_old` — drop? | You |

**Nothing above is fixed by the rewrite.** These are live data defects that exist right now.
The rewrite prevents *new* ones — transactions, idempotency, foreign keys, `DECIMAL`, and a
derived ledger instead of a mutated `balance` column — but the historical damage has to be
decided on, and corrected, by a human who knows the business.

---

## What this changes in the plan

- **Data remediation becomes its own phase, before Phase 5** (documents engine). We cannot build
  a derived ledger on top of duplicate payments — the ledger would faithfully reproduce the
  wrong numbers.
- The retype migration (`varchar` → `DECIMAL`/`DATE`) is **verified safe** — every value parses.
- Adding foreign keys requires the orphans cleaned first (Finding 3).
- Password rotation and forced reset is **urgent**, not Phase 1 (Finding 7).

---

## 🔴 FINDING 9 — B4 has already happened: two quotations share a number

Found while adding unique indexes to the document-number columns (2026-07-14).

`quotation_h` holds 2,119 rows but only 2,118 distinct `q_no`. Two different quotations, for two
different customers, are both numbered **`STQ-0`**:

| q_no | company | customer | date | total |
|---|---|---|---|---|
| STQ-0 | 1 | C-47 | 2025-09-19 | 50,000 |
| STQ-0 | 1 | C-102 | 2026-04-03 | 44,645 |

This is ISSUES **B4** — the duplicate-number race — not as a risk but as a fact. The legacy app takes
a number from a ticket table and never checks that it is unused, and there is no unique index to stop
the duplicate landing.

**Consequence for the rebuild:** the unique index was applied to `invoice_h`, `cn_h`, `po_h` and
`jobs_m` (all clean — zero duplicates). It **cannot** be applied to `quotation_h` while these two rows
stand.

**Not remediated, deliberately.** Per LEGACY-DATA-POLICY.md, legacy data is left as-is: somebody has a
PDF with STQ-0 printed on it, and renumbering it to make an index build is exactly the historical
rewriting that policy forbids. It surfaces in **Data Exceptions** for the business to resolve. Add
`quotation_h` to the unique-index list once they have.

---

## 🟠 FINDING 10 — Three payments are orphaned

Three rows in `payments` reference an `invoiceno` that does not exist in `invoice_h`. They therefore
cannot be attributed to a company, and their `company_id` is left NULL by the multi-company migration.

Guessing a company for them would file somebody's money under the wrong trading entity. They surface in
**Data Exceptions**.

---

## 🟡 FINDING 11 — The invoice prefix is not a constant

Company 2 (Smart Net) changed its invoice prefix from `SNI-` to **`26JUL_SNIN_`** on 2026-07-06 and is
still using it. The prefix encodes the year and month.

Critically, **the counter ran straight through the change**: `SNI-1556` was followed by
`26JUL_SNIN_1562`. The number and the prefix are independent, and the counter never resets.

The rebuild therefore stores the prefix as a **template** (`{YY}{MON}_SNIN_`) rendered at allocation
time, so August produces `26AUG_SNIN_` without an edit — while the counter keeps climbing. A literal
prefix would still be stamping JUL on invoices in August.

---

## 🔴 FINDING 12 — Two of Finding 1's duplicates survived its remediation

Found 2026-07-18, after the receivables ledger was rebuilt from the documents.

| Invoice | Total | Paid | Dates | Overpaid |
|---|---|---|---|---|
| STI-38 | 71,000 | 142,000 | 2025-06-12, 2025-06-29 | **71,000** |
| SNI-915 | 23,600 | 47,200 | 2025-01-21, 2025-02-15 | **23,600** |

**These are not new. They are Finding 1's own examples, and its remediation could not reach them.**
Both appear in the example table under Finding 1 — STI-38 at ×2 and SNI-915 at ×2. That table was
built by comparing what was recorded as paid against the invoice total, which is an overpayment test.
The remediation that followed used the *strict* definition instead — same invoice, same amount, **same
date** — and deleted 49 rows on that key. STI-38's payments are seventeen days apart and SNI-915's
three weeks apart, so neither matched, and both survived a cleanup that was reported as complete.

The stored `balance` said 0 afterwards, which is what a finished invoice looks like, so nothing
contradicted the report. The defect was invisible from either direction.

**The lesson for cutover:** a finding's headline query and its remediation query have to be the same
query, or the difference between them is exactly what survives. The new **Overpaid** rule measures
payments against the invoice total — the same test that produced Finding 1's table — so what the
report shows and what a remediation would target can no longer drift apart.

The new **Overpaid** rule measures the payments against the invoice total instead, so it finds them
however they are spread. It uses the same one-rupee tolerance as Finding 4 so rounding is not
reported as money.

**Not remediated.** Leaving the customer in credit is a true statement of the position; so is voiding
the payment that should not be there. That is a business decision, and both are available on the
payments screen.

---

## 🟠 FINDING 13 — 38 supplier invoices marked paid with nothing settling them

**1,435,253.01 across 38 invoices** (14 in company 1, 24 in company 2). `paymentstat = 'Paid'` with
no `supplier_inv_pay` row, so the payables outstanding excludes them — that figure is read from
`paymentstat` — while nothing records who was paid or when.

All 38 are `data_origin = 'legacy'`. The new app dual-writes a settlement row, so this is not its own
data being misread.

The payables side had no detection at all before this: the payables ledger holds no legacy rows, so
there was nothing for a ledger-based check to look at.

---

## 🟠 FINDING 14 — A supplier invoice settled twice

Supplier invoice **621** (`invno` 9784, supplier S-35, company 2, **165,000**) has two
`supplier_inv_pay` rows.

A legacy settlement carries no amount of its own — the row records which invoice was paid and when,
and the amount *is* the invoice's. Each row therefore stands for the whole invoice, which makes the
second one a second payment of the same 165,000.

---

## 🟠 FINDING 15 — 82 orphaned quotation lines across 16 quotations

The same defect as Finding 3, in `quotation_l`: 82 rows whose `qno` matches no quotation header.
Finding 3 only looked at `invoice_l`.

Between them, 690 orphaned line rows across 105 documents, carrying 4,678,439.54 of line value that
nothing counts and nothing can reach. Harmless to the accounts, fatal to any foreign key.

`po_l` and `cn_l` are clean — zero orphans in both.
