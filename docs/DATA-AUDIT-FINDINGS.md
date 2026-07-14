# Data Audit — Findings

Run 2026-07-14 against `smartnet_invsys_dev` (a copy of live production) — read-only.
Server: MariaDB 10.11. 49 tables, ~2,485 invoices, 12,598 invoice lines, 2,275 payments.

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
