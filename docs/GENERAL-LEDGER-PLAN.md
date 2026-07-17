# General Ledger — plan

A double-entry general ledger: one place where **every money movement posts a balanced debit/credit** to
accounts, so the business can see a **trial balance**, a **profit & loss**, and a **cash/bank position** — the
things the accounts-receivable and accounts-payable sub-ledgers, on their own, cannot show.

The legacy app had **no** general ledger — this is genuinely new, not a strangler adoption. It sits *on top of*
the money events already built in Phases 5–7, and reuses their discipline: **append-only, derived balances,
nothing mutated in place**.

Parent: [DEVELOPMENT-PLAN.md](DEVELOPMENT-PLAN.md) · builds on [PHASE-7-PLAN.md](PHASE-7-PLAN.md)

---

## What must already be true (all built)

Every money event exists and is the single source of its own truth:

| Event | Where | Already carries |
|---|---|---|
| Invoice raised | `invoice_h` + `receivables_ledger` (Charge) | net, VAT, total |
| Customer receipt | `customer_receipts` + `receivables_ledger` (Payment) | amount, method |
| Credit note | `cn_h` + `receivables_ledger` (Credit) | net, VAT, total |
| Supplier invoice | `supplier_invoice` + `payables_ledger` (Purchase) | net, VAT, total |
| Supplier payment | `supplier_payments` + `payables_ledger` (Payment) | amount, method |
| Expense | `expense_tr` | net, VAT, total, method |
| **Cheque** | `cheques` | **print-only — never a money event** (it is the *method* of a payment/expense; the payment/expense is the event) |

So the GL does not invent data — it **classifies** existing events into accounts.

---

## The model

- **`gl_accounts`** — the chart of accounts: `id, code, name, type (Asset|Liability|Income|Expense|Equity),
  is_cash_or_bank`. Seeded with a default chart; new accounts can be added. Company-scoped where it matters
  (Cash/Bank per company), shared otherwise.
- **`gl_entries`** — a journal entry (header): `id, company_id, date, source_type, source_id, description`.
  One per money event. `source_type`/`source_id` link back to the event (idempotent — an event posts once).
- **`gl_lines`** — the debit/credit lines: `id, entry_id, account_id, debit, credit`. **Σ debit = Σ credit**
  per entry, enforced on write. A balance is `Σ (debit − credit)` over an account's lines — **derived**, never
  stored (the same rule as the sub-ledgers).

## The postings (each event → one balanced entry)

| Event | Debit | Credit |
|---|---|---|
| Invoice | Accounts Receivable (total) | Sales (net) · Output VAT (vat) |
| Customer receipt | Cash/Bank (amount) | Accounts Receivable (amount) |
| Credit note | Sales (net) · Output VAT (vat) | Accounts Receivable (total) |
| Supplier invoice | Purchases (net) · Input VAT (vat) | Accounts Payable (total) |
| Supplier payment | Accounts Payable (amount) | Cash/Bank (amount) |
| Expense | Expense[category] (net) · Input VAT (vat) | Cash/Bank (total) |

**Cash vs Bank** is chosen by the event's method: `Cash` → the Cash account; `Cheque`/`Bank`/`Online` → the
Bank account. (A cheque still does not post — its *payment/expense* posts to Bank.)

## Reports

- **Trial balance** — every account with its debit/credit balance; total debits = total credits (the proof
  the ledger is consistent).
- **Profit & loss** — Income − Expenses for a period.

(A cash/bank position report is **not** required — the Cash/Bank accounts still appear on the trial balance.)

---

## Build order (slices)

1. **Chart of accounts** — `gl_accounts` + a seeded default chart + a small admin screen (list/add/rename).
2. **Posting engine** — `gl_entries`/`gl_lines`, a `IGeneralLedger.Post(...)` that writes one balanced entry
   idempotently (keyed on source_type+source_id), wired into each money-event service. **Backfill** the
   historical events (a migration deriving postings from the existing documents/sub-ledgers).
3. **Reports** — trial balance, then P&L (web + export).

---

## Decisions to settle before slice 1

- **Expense accounts:** one **Expenses** account, or **one account per expense category**? (Per-category gives
  a categorised P&L; one account is simpler.)
- **Cash/Bank accounts:** a single **Bank** account, or one **per bank** (from the cheque/payment bank)?
- **Backfill:** post the **historical** events into the GL at cutover (a migration), or start the GL **from
  today** forward only?
- **First report:** which of trial balance / P&L / cash position do you want first?

## What it does NOT do (for now)

- No manual journal entries / adjustments UI (postings come from events; a manual-journal screen can follow).
- No accruals, depreciation, or period-close/lock — a straight transactional ledger first.
- Cheques never post — reaffirmed.
