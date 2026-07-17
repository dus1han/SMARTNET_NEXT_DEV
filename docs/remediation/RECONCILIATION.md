# Reports reconciliation & Finding 1 remediation

Run 2026-07-17 against `smartnet_invsys_dev`. This records (a) the cross-report reconciliation checks that
confirm the dashboard, P&L, Trial Balance and the other reports agree, and (b) the one-time data remediation
of DATA-AUDIT **Finding 1** (duplicate customer payments).

**Everything here that touched data was done on the dev copy. It must be repeated on live** — the migration
`Phase8RemediateDuplicatePayments` carries the fix; the checks below are how you confirm live matches dev.

---

## 1. Reconciliation checks (all pass)

The dashboard and the Sales report read the legacy `invoice_h` (gross, incl. VAT); the P&L and Trial Balance
read the new `gl_*` ledger (net revenue). They are *different measures by design* and reconcile through VAT and
credit notes. Verified for the current month, all companies:

| Check | A | B | Result |
|---|---:|---:|:--:|
| Sales — dashboard gross vs GL invoice postings | 1,470,867.39 | 1,470,867.39 | ✅ |
| Revenue — P&L vs Trial Balance Sales (4000) | 1,301,644.55 | 1,301,644.55 | ✅ |
| Output VAT — (invoices − credit notes) vs TB (2100) | 75,578.04 | 75,578.04 | ✅ |
| Input VAT — supplier invoices vs TB (1200) | 25,627.12 | 25,627.12 | ✅ |
| Purchases — supplier invoices net vs TB (5000) | 160,312.88 | 160,312.88 | ✅ |
| Trial balance — total debits vs credits | balanced | balanced | ✅ |

**Dashboard → P&L bridge** (now shown on the P&L screen and in its Excel export):
`Gross invoiced sales 1,470,867.39 − VAT 89,862.84 − credit notes 79,360.00 = Revenue 1,301,644.55`.

The only surface that did **not** reconcile was **Outstanding** — caused entirely by Finding 1 (below), not by
any reporting logic.

---

## 2. Finding 1 remediation — duplicate customer payments

**Cause.** Legacy `CusPaymentsController.savePay` inserted a payment then ran
`UPDATE invoice_h SET balance = balance - amount` with no transaction / idempotency / duplicate check. A
double-clicked Save recorded the same payment (same invoice, amount, date) 2–6 times and subtracted it each
time.

**Scale (dev copy):** 44 duplicate groups · 49 extra payment rows · Rs. 1,575,374 overstated ·
45 invoices left with a negative balance totalling −Rs. 1,554,424.

**Two shapes, both corrected by recomputing `balance = total − Σ(surviving payments)`:**

- **45 negative balances → 0.** 43 had duplicate rows; 2 (STI-665, STI-961) had no duplicate *row* but a
  double-*subtracted* balance — caught because they were negative.
- **SI-35 — a duplicate that did not turn the invoice negative.** Total 144,750; a 50,000 payment was
  duplicated, so genuine payments are 94,750 and the invoice really owes **50,000**. **Confirmed correct by
  the business (2026-07-17):** the customer does owe it. Remediated to a 50,000 balance, and a receivables
  opening entry was created for it (it had none, its legacy balance having been 0).

**What the migration does** (`Phase8RemediateDuplicatePayments`, idempotent):
1. Delete the 49 duplicate payment rows (keep `MIN(id)` per group).
2. Delete their 49 GL `LegacyPayment` entries (balanced, so the TB stays balanced).
3. Recompute every affected invoice's balance = `total − Σ(surviving payments)`.
4. Reseed the receivables opening entries to the recomputed balances (drops the 45 negatives, adds SI-35's 50,000).

### Verification (dev, after applying fresh from the original state)

| | Before | After |
|---|---:|---:|
| Duplicate payment groups | 44 | **0** |
| Negative-balance invoices | 45 | **0** |
| `payments` rows | 2,275 | **2,226** |
| GL `LegacyPayment` entries | 2,275 | **2,226** |
| Trial balance (Dr = Cr) | 356,188,944.56 | **354,613,570.56** (−1,575,374.00) |
| Dashboard Outstanding | 10,033,563.79 | **11,637,987.79** |
| Outstanding report | 11,587,987.79 | **11,637,987.79** |
| Dashboard vs report | ✗ differ | **✅ match** |
| SI-35 balance | 0 (masked) | **50,000** (+ receivables opening) |

---

## 3. Running this on live

1. **Take a pre-run snapshot on live** of exactly what the migration removes/changes — capture the duplicate
   payment rows, their GL entries/lines, the affected `invoice_h` balances, and the affected
   `receivables_ledger` openings. The dev run's snapshot is `finding1-backup-dev.txt` in this folder — it is
   **git-ignored** (it holds row-level payment data and staff names), so regenerate a fresh one on live; it is
   the only rollback, as the migration's `Down` is intentionally one-way.
2. Apply migrations to live as usual (`dotnet ef database update … --context SmartnetDbContext`). The counts on
   live will differ from dev's; **re-run the checks in §1 and §2 against live** and confirm: 0 duplicate
   groups, 0 negative balances, trial balance balanced, dashboard Outstanding == Outstanding report.
3. Confirm SI-35 (or its live equivalent) landed as intended before telling the customer they owe it.

> Note: the live *production* legacy DB (`smartnet_invsys`) is still written by the old app. If duplicate
> payments can still be created there before cutover, re-check for new duplicate groups at cutover — the new
> app's payment path is idempotent and does not reproduce the defect.
