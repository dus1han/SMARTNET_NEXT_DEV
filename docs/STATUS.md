# Where the build actually is

Verified against the code on **2026-07-20**, not against the plans. Written because the plans had
drifted: Phases 1–6 carry no completion markers at all despite being finished, Phase 7 claimed a
column drop that never happened, Phase 1 still declared an "open blocker" resolved months ago, and
several documents insist cheque printing is future work when it shipped.

**This file is the answer to "what is done and what is left."** The phase plans remain the record of
*why* each thing was built the way it was; they are no longer the record of *whether* it was.

---

## Phases

| Phase | State | Notes |
|---|---|---|
| 0 · Foundations | **Done** | Monorepo, CI, compose, EF scaffold. Deviation: nginx no longer falls back to the legacy app — this host has no legacy app. |
| 1 · Auth, users & settings | **Done**, one gap | Argon2id + hash-on-login, JWT cookie, 35 permissions → policies, user admin, settings. **`document_templates` was never built** — see Phase 8. |
| 2 · Design system & shell | **Done** | Generated client, DataTable/form abstractions, History tab, company switcher. The line-item prototype route has been removed now that Phase 5 shipped the real editor, as its own header said it should be. |
| 3 · Master data | **Done** | Customers, suppliers, items, stock, Excel export. |
| 4 · Dashboard & reports | **Done** | **10/10 reports**, plus trial balance, P&L and data exceptions. Dunning queue + `email_log` built but **sending is off** — deliberately, until the balances are corrected. |
| 5 · Documents engine | **Done** | Tax engine on `decimal`, transactional numbering, ledger-derived balances, versioning, quote → invoice. |
| 6 · Purchasing & service | **Done** | POs, supplier invoices, job cards, structured contacts. **GRN deferred** out of scope — no phase assigned. |
| 7 · Money & documents | **5 of 6 slices** | Receipts, cheques, expenses, document storage, notes all shipped. **Slice 6 not started.** |
| 8 · PDF templates | **Substantially done** | All six QuestPDF templates exist and **cheque printing works** — measured 7×3.5in, every print audited. Gaps: no `document_templates` configurability, and no rendering tests. |
| 9 · Cutover | **Not started** | Only artifact is `MIGRATION-DATA-CHECKS.md`. |
| GL (unnumbered) | **Done** | Chart of accounts, posting engine, backfill, trial balance, P&L. Never assigned a phase number. |

---

## What is actually pending

### 1. Phase 7 slice 6 — the phase cannot close without it
- **The acceptance case is not automated.** A receipt across two invoices settling both, idempotent
  and transactional, is the stated exit criterion for the whole phase and rests on manual checks.
- **`POST /api/dev/seed-payment` still ships.** `Program.cs:254-287`, Development-only but
  **anonymous and it writes ledger rows**. Slice 6 was where it retires. `e2e/invoice.spec.ts:53`
  still depends on it.
- **No Phase 7 reconciliation section** in `legacy-analysis/RECONCILIATION.md`.
- **No `phase7.spec.ts`.**

### 2. Test-shaped holes
- **There is no HTTP-level test layer at all** — no `WebApplicationFactory`, no `TestServer`. Every
  one of the 663 API tests is service-level. The middleware pipeline (auth, must-change-password,
  correlation id, rate limiting, CORS, exception handling) is proven only by four browser tests.
- **No PDF rendering tests.** `Smartnet.Tests/Pdf/` contains only `AmountInWordsTests.cs`, so six
  templates that produce customer-facing documents have no test asserting what they render.
- `Smartnet.Tests/UnitTest1.cs` is still the scaffold file.

### 3. Known defects, unfixed
- **The audit log records EF's temporary key on Create rows** — `Id: -9223372036854775000` instead of
  the real id. Affects every entity (visible on `CustomerContact` and `UserNote`). `entity_id` is
  correct so records stay findable; only the `changes` JSON is wrong.
- **`audit_log` is not append-only on dev — confirmed, with the cause.** The app's own user reports:

  ```
  GRANT ALL PRIVILEGES ON `smartnet_invsys_dev`.* TO `smartnet_invsys_next`@`%`
  ```

  Two things follow. There are **no table-level grants at all**, so `infra/sql/audit-log-grants.sql`
  was never run here. And even if it were, that schema-wide `GRANT ALL` would outrank it — MySQL
  takes the union of privileges across levels, which is exactly what the script's own closing note
  warns about. `DELETE FROM audit_log` as the application user succeeds.

  **Production is unchecked** — that user holds rights on `smartnet_invsys_dev` only, so production
  uses different credentials. Run `SHOW GRANTS FOR '<prod app user>'@'%'` there. If it shows a
  schema-level `GRANT ALL` or any `UPDATE`/`DELETE` on the schema, the log is rewritable by anything
  holding the app's password, and `AUDIT.md`'s central claim does not hold where it matters.

  **Fixing it needs a DB admin.** The app user has no `GRANT OPTION`, so it cannot narrow its own
  privileges: run the grants script as an admin, then narrow the schema-wide grant to the specific
  tables the app needs.
- **`/api/dashboard/analytics` and `/api/reports/companies` return 500 on dev**, apparently from
  remote-DB timeouts rather than logic.

### 4. Legacy data — decisions, not code

**Dev is at 8 exceptions, down from 155.** Four were decided and worked to zero on 2026-07-20; every
one still has to be repeated on live, and the scripts are in `infra/sql/`. Full detail, including the
checks made before each, is in [MIGRATION-DATA-CHECKS.md](MIGRATION-DATA-CHECKS.md).

| Fixed on dev | How | Live |
|---|---|---|
| 105 orphaned line groups (690 rows) | `remove-orphaned-lines.sql`, archived first | **to run** |
| 38 supplier paid-not-settled | `settle-supplier-paid-not-settled.sql`, rows marked `RECONSTRUCTED` | **to run** |
| 3 payments naming no invoice (70,381) | `remove-orphaned-payments.sql`, archived with their GL | **to run** |
| STI-1068 paid, no payment (21,500) | The app's audited correction, not a script | **to do** |

**The eight that remain**, none of which is a script's job:

- **`STQ-0` — the only one still blocking cutover.** Two quotations share the number, so the unique
  index cannot be built on `quotation_h`. Somebody holds a printed PDF with it on, and renumbering to
  make an index build is the historical rewriting LEGACY-DATA-POLICY forbids. Business decision.
- **2 overpaid** — STI-38 (71,000 paid twice, seventeen days apart) and SNI-915 (23,600, three weeks
  apart). Either void the payment that should not be there, or leave the customer in credit — both are
  legitimate; zeroing a balance is not.
- **4 lines ≠ header** — STI-1150 is the serious one (header 12,041 against 1,916 of lines); the other
  three are gaps of 700, 240 and 60. Each needs a per-invoice decision about which side is right.
- **1 supplier settled twice** — invoice 9784, 165,000 recorded as settled twice, each settlement
  standing for the whole invoice.

And this, which is easy to miss: **a clean duplicate-payment count does not mean nothing is
overpaid.** The remediation matched same invoice + same amount + *same date*; STI-38 and SNI-915 are
dated weeks apart, survived it, and are the two still on the list. Check the Overpaid tile as well.

### 5. Security items outstanding
- **The legacy `notes` table is a plaintext credential store** — banking, email, iCloud and
  third-party logins, still being appended to as of July 2026. It must not be carried into the Legacy
  Archive as it stands. See `MIGRATION-DATA-CHECKS.md`.
- **Plaintext `password` column is still live** and still written on password change — correct for
  the dual-write window, dropped at cutover.
- **Data Protection keys are not persisted.** Carried unresolved since Phase 1. A redeploy invalidates
  every stored SMTP password.

### 6. Phase 8 gaps
- **`document_templates` does not exist.** Promised in both Phase 1 and Phase 8; templates are driven
  by `Company` alone. There is no per-company template settings surface.
- **Whether the cheque overlay registers on physical stationery is unverified in the repo.** The page
  size is measured, but only a real cheque through a real printer settles it.

### 7. Phase 9 — all of it
Drop the plaintext password column · `varchar → DECIMAL/DATE` retype · drop `docstore.pdfdoc`
(bytes are already on disk; C4's bloat reclamation is blocked on this) · remove the Crystal and
legacy dual-writes · delete `DBConnect` · archive the legacy app · numbering initialisation ·
work `MIGRATION-DATA-CHECKS.md` to zero.

### 8. Deferred with no home
- **GRN (goods received note)** — deferred out of Phase 6, never assigned a phase. Until it exists a
  PO posts no stock and all stock-in goes through a manual adjustment.
- **Turn dunning on**, once balances are trusted.
- **`LegacySchema.cs` needs a real `mysqldump --no-data` baseline** — its own comment says "before
  staging".
- **`documents.entity_type` is dead** — every row is NULL, no caller sets it, and its doc comment
  describes a lowercase vocabulary that conflicts with the PascalCase one `audit_log` and the notes
  module use. Wire it up or delete it; a column that is always NULL with two competing vocabularies
  is worse than neither.
- **Tax rates are read-only in the UI.**
- **Price the ~500 items**, and explain `c_form`.

---

## Suggested order

1. **Fix the `audit_log` grants** — dev is confirmed broken, production is unknown. Needs a DB admin,
   takes minutes, and until it is done the audit trail is not evidence.
2. **Phase 7 slice 6.** One task closes the highest-severity item (`seed-payment`) and the largest
   missing test.
3. **Get the five legacy-data decisions answered** — the orphaned lines block foreign keys, which is
   structural and gets more expensive the longer it waits.
4. **Add a `WebApplicationFactory` fixture.**
5. **Fix the audit interceptor's temporary-key bug.**
6. Then Phase 9.
