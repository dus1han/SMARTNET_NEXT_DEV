# Phase 7 — Money & documents

~2.5 weeks. Customer payments, the cheque register, expenses + categories, document storage, and per-entity
notes — the modules that finish the *money* side (the receivables ledger has waited since Phase 5 for a real
payments UI) and add the first *file* handling the new app has. This is where the last two of the legacy
app's most dangerous money bugs are closed for the customer side (the `balance = balance − amount` mutation
and the duplicate-payment race, B2/B3/Finding 1), and where the unrestricted-upload / BLOB-in-MySQL document
holes (A8/C4) are shut.

Parent: [DEVELOPMENT-PLAN.md](DEVELOPMENT-PLAN.md) · Previous: [PHASE-6-PLAN.md](PHASE-6-PLAN.md)

**Exit criterion:** a customer **receipt allocated across two open invoices** settles both — each invoice's
**derived** balance falls, the receipt is **idempotent** (a double-click cannot double-pay — Finding 1) and
**transactional**, and the legacy `payments` rows and `invoice_h.balance` are **dual-written** so every legacy
reader (the outstanding report) stays correct. That one case exercises the whole of what the legacy payment
screen got wrong; if it does not pass, slice 1 is not done.

---

## What must already be true before slice 1

Phase 7's money side is mostly *reuse* — the ledger it settles against already exists. Confirmed present
(Phases 1–6, read against the source 2026-07-17):

- **The receivables ledger is the settlement target, and it is done.** `LedgerEntry`
  (`Smartnet.Domain/Ledger`, types `OpeningBalance | Charge | Payment | Credit`, signed `decimal Amount`,
  nullable `InvoiceId`), `IReceivablesLedger.BalanceForCustomerAsync`, and `receivables_ledger` (a real new
  table). A customer's balance and a **per-invoice outstanding** are *derived* (`Σ` entries grouped by
  `InvoiceId`), never stored. **Every existing invoice already has an `OpeningBalance` entry** (the Phase 5
  seed), so a legacy invoice's outstanding is derivable from the ledger too — a new payment can settle a
  legacy invoice.
- **The per-invoice outstanding query exists.** `InvoicesController.List` groups the ledger by `InvoiceId`
  (`Σ amount`) for the invoice list, and `Get` sums it for one invoice. Slice 1's "a customer's open
  invoices" is that query, filtered by `CustomerId` and kept where the sum ≠ 0.
- **The supplier-payment path is a working template for the receivables side.** Phase 6's
  `SupplierInvoiceService.RecordPaymentAsync` already does exactly this shape on the *payables* ledger:
  guard-in-transaction → post a `Payment` entry (negative) → dual-write the legacy row → flip/derive status.
  Customer payments mirror it on the receivables ledger, with allocation across invoices.
- **The adoption pattern is settled.** The hand-written additive migration (`Phase5AdoptInvoices`,
  `Phase6SupplierInvoicesAndPayables`) — surrogate `id`, typed `decimal`/`date` columns beside the legacy
  `varchar`, `data_origin`, audit columns, dual-write, a `data_origin='new'` query filter — is the template
  the cheque and expense adoptions copy. `LegacyValue.Money/.Date` parse legacy `varchar` reads defensively.
- **`company_id` is already on these tables.** The multi-company migration added `company_id` to `cheques`
  and `expense_tr` (backfilled from their `company` varchar) and to `payments` (derived through the invoice).
  It is **not yet mapped** on the `Payment`/`Cheque`/`ExpenseTr` scaffolded entities, and none has
  `data_origin` — slice work adds both.
- **The legacy tables have live new-app readers.** The Phase 4 reports read `cheques` (`ChequeReport`),
  `expense_tr` + `exp_cat_m` (`ExpenseReport`) and `payments` (the outstanding report's `PaidAfterAsync`
  rolls balances back through it) via `SmartnetLegacyDbContext`. So new writes in these modules **must keep
  dual-writing the legacy tables** — the strangler rule — until those readers are ported.

**Two stopgaps this phase retires.** (1) The Phase 5 `data_origin='legacy'` scoping of the legacy
`CusPaymentsController` (a legacy-side edit that kept it off new invoices) — Phase 7 replaces that screen
entirely, so the new payments module owns customer payments end to end. (2) The Development-only
`POST /api/dev/seed-payment` used by the Phase 5 E2E — once a real payment endpoint exists, the E2E uses it.

---

## What the legacy source says before we plan anything

Read against the live controllers at `C:\Users\Saboor.a\Desktop\SMARTNET_DEV\Smart_InvSys\Controllers`,
not assumed. All four modules share the raw-SQL, no-transaction, string-money habits Phase 5/6 already ended
elsewhere; the money ones repeat the exact defects the ledger exists to close.

1. **Customer payments mutate `invoice_h.balance` in place, non-transactionally, with no idempotency.**
   `CusPaymentsController.savePay` reads `invno, amnt, date, cuspaymethod, refno` and issues **two separate**
   `inupdel` calls: `INSERT INTO payments(invoiceno, amount, paymentrecdate, enteredby, entereddt, paym,
   payref) …` then `UPDATE invoice_h SET balance = balance − '{amnt}' WHERE invoiceno = '{invno}'`. A failure
   between them diverges the payment record from the balance (B2); the balance is the single source of truth
   and can drift with no ledger to rebuild it from (B3); and there is **no idempotency** — the modal is not
   disabled on submit, so a double-click double-inserts and double-decrements (Finding 1, the Rs 1.55M
   duplicate mechanism). `deletepay` hard-deletes the row and does `balance = balance + '{amnt}'` with the
   **client-supplied** amount, unverified against the stored payment. Overpay is blocked only in the browser.
   The `payments` table: `id, invoiceno, amount, paymentrecdate, enteredby, entereddt, paym` (method),
   `payref` (reference) — no customer column (reached via the invoice), one payment → one `invoiceno`.
   `getCusOutInv` lists open invoices as `invoice_h.balance > 0` across **all** customers (no scope).
   Methods offered: Cash / Cheque / Online Payment.
2. **The cheque register is a standalone log that touches no balance.** `ChequeController` writes `cheques`
   (`chequedate, payto, amount, company, duedate, createdby, createddt, printeddt, bank, chkno, entry,
   supcode`) — a record of cheques *written*, affecting no ledger or balance. `entry` is `Manual` (free-text
   payee) or `Supplier` (stores `supcode` + the supplier name in `payto`); there is **no** link to invoices,
   payments or expenses. `deletecheque` hard-deletes (even a printed one, server-side). `printcheque` is a
   **Crystal overlay on pre-printed stationery** — it pushes each date digit into its own box
   (`DayDigitOne/Two`, `MonthDigitOne/Two`, `YearDigitOne/Two`, `Pay`, `Amount`, `AmountText`) — so it must
   stay pixel-exact and is **Phase 8**, not here. Money is `varchar`, parsed with `Convert.ToDouble`.
3. **Expenses are a flat append-only log — no ledger, no balance.** `CExpensesController` writes `expense_tr`
   (`id, exp_cat` → `exp_cat_m.id`, `expense_date, expense_desc, expense_amount, paymentm` (method),
   `payment_ref, addedby, addeddt, company` → `companies_m.id`) and manages categories in `exp_cat_m`
   (`id, expcatname`). `saveExpense`/`deleteExpense` are single raw statements; delete is a **hard delete**;
   `expense_amount` is text, never parsed or summed in code. It exists only to feed the expense report.
4. **Document storage is an unrestricted upload storing whole files as MySQL BLOBs.** `DocStoreController.
   uploaddoc` takes any posted file (**no** extension/MIME/size check — A8), uses the **client-supplied
   filename** under the web root (`~/Files/Uploads/{guid}/{clientName}`), then stores the bytes as a BLOB in
   `docstore.pdfdoc` (C4) via the one parameterised path (`DBConnect.addpdf`). `generateDocPDF` re-writes the
   BLOB back under the web root on **every** view and never cleans it up (C3). `docstore` is
   `id, title, addeddate, addedby, docext, pdfdoc` — **standalone**, attached to no entity, keyed by id +
   free-text title. Delete is hard. (The same upload flaw in `WProductsController` is moot — the web
   catalogue was dropped, decision 1.)

The money modules are the same `double`/`varchar`, balance-in-place, non-transactional shape Phase 5 replaced
for invoices; Phase 7 replaces it for the customer-payment side and adds the two supporting registers, then
builds real file handling the legacy app never had safely.

---

## Slice 0 — decisions taken before any code

Four settled from the research + your answers (2026-07-17).

**A. Customer payments are a *receipt* allocated across open invoices, on the receivables ledger, dual-writing
the legacy shadow (your decision).** There is no one-payment-one-invoice limit: a **`CustomerReceipt`** (amount
received, date, method, reference) carries one or more **allocations**, each against an open invoice. Saving,
in **one transaction**: for each allocation post a `Payment` ledger entry (negative `Amount`, the invoice's
`InvoiceId`) — the new source of truth, from which every balance is derived — **and** dual-write the legacy
shadow: a legacy `payments` row (`invoiceno`, `amount`, method, reference) *and* `UPDATE invoice_h SET
balance = balance − amount` for that invoice. So the derived ledger owns the truth while every legacy reader
(the outstanding report's `PaidAfterAsync` over `payments`, any reader of `invoice_h.balance`) stays correct
and the legacy **detail views read the same** — "not ledger-only", as you asked. It settles **new and legacy
invoices alike** (a legacy invoice's outstanding is its seeded `OpeningBalance` plus any new payments).
Overpay is **rejected server-side** (an allocation cannot exceed an invoice's derived outstanding), and the
receipt carries an **idempotency key** so a double-submit cannot create a second one — Finding 1, closed. A
void reverses the allocations through compensating ledger entries and re-adds the legacy balance, never by
erasing history (the legacy `deletepay` hard-delete is not ported).

**B. The cheque register is a standalone register, adopted additively; printing is Phase 8.** Adopt `cheques`
onto the new side exactly as the documents were — surrogate `id`, a typed `decimal` amount and `date`s beside
the legacy `varchar`, `data_origin`, audit columns; `company_id` already exists. It records a cheque *written*
and touches **no ledger or balance** (faithful — a cheque is not a payment allocation). `entry` stays
`Manual | Supplier` with an optional `supplier_id` (the new surrogate) beside the legacy `supcode`. New rows
dual-write the full legacy `cheques` row so `ChequeReport` keeps reading. **Cheque *printing* is Phase 8** —
it overlays pre-printed stationery and must be pixel-exact; Phase 7 is the register (create / list / read /
void), and `printeddt` is carried but set by the Phase 8 print path.

**C. Expenses + categories are adopted additively — a flat log, `decimal`, soft-deleted.** Adopt `expense_tr`
(typed `decimal` amount + `date`, `data_origin`, audit; `company_id` exists) and `exp_cat_m` (categories as
mini-master-data). No ledger — an expense is a recorded outgoing, not a double-entry posting. `saveExpense`
becomes a validated, audited write dual-writing the legacy row for `ExpenseReport`; delete is **soft** (the
legacy hard delete is not ported). Categories get add/rename (the legacy `exp_cat_m` had only insert).

**D. Document storage is the filesystem outside the web root, behind an abstraction, with the legacy BLOBs
migrated out into it (your decision).** An `IDocumentStorage` abstraction with a **local filesystem**
implementation that writes **outside the web root** under **server-generated names** (never the client
filename — A8), gated by an **extension + content-type whitelist and a size cap**; only *metadata* goes in a
new `documents` table (`id, title, original_name, stored_name, content_type, size_bytes, entity_type,
entity_id, …` + audit), never the bytes (C4), and files are streamed on download, not re-materialised under
the web root (C3). Documents are **attachable to an entity** (customer / invoice / job card …) via a nullable
`entity_type`/`entity_id`, so a document lives on the thing it is about — but standalone (both null) is
allowed. **The existing legacy `docstore` BLOBs are migrated out, then the BLOB column is dropped (your
decision):** a one-off tool reads each `docstore.pdfdoc`, writes the bytes to the new storage under a
generated name, and creates a `documents` row — so every existing document exists in the filesystem store —
and, once every BLOB is verified materialised, a migration **drops `docstore.pdfdoc`**. The bytes now live on
disk (plus the `documents` metadata), so the in-DB copy is redundant, and dropping it reclaims the database
bloat that is the whole of C4. This is a **deliberate, confirmed exception** to "leave legacy data as-is": no
document is lost (each is written to a file *before* the column goes), only the redundant BLOB is removed; the
`docstore` metadata rows are kept, read-only, via the Legacy Archive. The abstraction means swapping the local
backend for S3/MinIO later is a config change, not a rewrite.

**E. Notes are per-entity (your decision).** A `notes` table (`id, entity_type, entity_id, body, …` + audit)
and a small reusable component that attaches timestamped, audited notes to a customer, an invoice, a job card,
etc. — a note lives on the record it is about, with who and when. The trivial legacy standalone `Note` table
is not ported (it carries nothing worth migrating); it stays readable via the Legacy Archive.

---

## Slice 1 — Customer payments (receipts & allocation) · ~1 week · *the spine*

> **Built and shipped** (commits `4f4026c` backend, `0310c3b` web). `CustomerReceipt`/`ReceiptAllocation`
> on new tables; `CustomerReceiptService` posts a `Payment` ledger entry per allocation and dual-writes the
> legacy `payments` row + `invoice_h.balance` — set to the **derived** outstanding (an absolute value off the
> ledger, not the legacy in-place decrement, so the shadow can't drift). Idempotency key dedupes a resubmit
> (Finding 1); over-allocation refused; soft, reason-gated void reverses each allocation through a
> compensating entry. `/payments` web module (list, allocate-across-invoices form, detail + void). Tests
> green (444); migration applied to the dev DB. One refinement to the plan below: the legacy `balance` is
> **set to the derived outstanding**, not decremented, which is strictly better than the `balance − amount`
> sketched here.

The receivables mirror of the Phase 6 supplier-payment path, with allocation. This is where the customer-side
money bugs close.

- **The aggregate.** A `CustomerReceipt` (company, customer, date, amount, method, reference, idempotency key,
  `data_origin`, audit) with `ReceiptAllocation` lines (invoice id + amount). A new table
  (`customer_receipts` + `receipt_allocations`) via `CreateTable` — receipts are a new concept, not a legacy
  adoption (the legacy `payments` table is the *shadow*, dual-written, not the source of truth).
- **The save pipeline.** One transaction: validate each allocation ≤ the invoice's **derived** outstanding
  (reject overpay); for each allocation post a `Payment` ledger entry (`InvoiceId`, negative `Amount`) **and**
  dual-write a legacy `payments` row + `UPDATE invoice_h SET balance = balance − amount`; write the receipt +
  allocations; commit. An **idempotency key** on the receipt stops a double-submit creating a second one
  (Finding 1). Settles new and legacy invoices alike.
- **A void** (reason-gated, `row_version`-guarded) reverses each allocation through a compensating ledger
  entry and re-adds the legacy balance — never a hard delete.
- **API + web.** `CustomerReceiptsController` (list, read with its allocations, create, void); the open-invoice
  picker = the grouped ledger query filtered by customer. Web: a receipt screen (pick customer → its open
  invoices with derived outstanding → allocate an amount across them → save), a receipts list and a read view.
  Replaces the legacy `CusPayments` screen; the `data_origin='legacy'` stopgap is retired.

**Exit:** the acceptance case — a receipt allocated across two open invoices settles both, idempotent and
transactional, ledger + legacy shadow in step — proven by unit + integration tests.

---

## Slice 2 — Cheque register · ~0.4 week

> **Built and shipped.** Adopted `cheques` additively (id promoted to PK; typed amount/dates + supplier_id beside the legacy varchars; company_id already existed). `ChequeService` dual-writes the legacy row for `ChequeReport`; soft reason-gated void. `/cheques` web module (list new+legacy, Manual/Supplier form, detail + void). No printing at the time (Phase 8) — **cheque printing has since been built**: `ChequeRenderer` + `ChequesController`, page size set to a measured 7×3.5in, every print audited and reprints counted. Tests green (469).


- **Adopt `cheques`** additively (surrogate id, typed `decimal`/`date`, `data_origin`, audit; `company_id`
  exists; optional `supplier_id` beside legacy `supcode`). A `Cheque` aggregate, EF config with the legacy
  shadow, `data_origin='new'` query filter.
- **A `ChequeCreator`** — a validated, audited write dual-writing the full legacy `cheques` row for
  `ChequeReport`. Standalone; touches no balance. Soft, reason-gated void (not the legacy hard delete).
- **Web:** a cheque list (new + legacy), a new-cheque form (Manual/Supplier, bank, cheque no, dates), a read
  view. ~~**No printing** (Phase 8) — `printeddt` is shown, not produced.~~ **Superseded:** Phase 8 built the cheque overlay; the detail page prints and `printeddt` is produced.

**Exit:** a cheque is recorded and read back (new + legacy), voided softly; `ChequeReport` still reads it.

---

## Slice 3 — Expenses & categories · ~0.4 week

> **Built and shipped.** Adopted expense_tr (dropped its all-zero id for a real surrogate; typed amount/date + category_id beside the legacy varchars; company_id existed) and exp_cat_m (id promoted to PK, audit added). ExpenseService dual-writes the legacy row for ExpenseReport; soft void; categories add/rename. /expenses web (list new+legacy, form, category manager). Tests green (484).

- **Adopt `expense_tr` + `exp_cat_m`** additively (typed `decimal` amount + `date`, `data_origin`, audit;
  `company_id` exists). An `Expense` aggregate and an `ExpenseCategory` mini-master.
- **Write path + categories.** Validated, audited expense create dual-writing the legacy row for
  `ExpenseReport`; soft delete. Category add/rename.
- **Web:** an expenses list (new + legacy, filterable by category/company), a new-expense form, and category
  management.

**Exit:** an expense is recorded against a category and read back; delete is soft; `ExpenseReport` still reads it.

---

## Slice 4 — Document storage · ~0.6 week · *closes A8 / C3 / C4*

> **Built and shipped**, with one deliberate deferral. `IDocumentStorage` + `LocalFileDocumentStorage`
> (outside the web root, server-generated names, extension/content-type whitelist, size cap); a `documents`
> metadata table (no bytes) with optional `entity_type`/`entity_id`; upload / list / streamed download /
> reason-gated soft delete, all behind `docstorage`. `/documents` web module (upload dialog, paged table,
> preview dialog). `tools/DocstoreMigrate` materialises the legacy BLOBs (dry-run by default, idempotent
> via a unique `legacy_docstore_id`, refuses production).
>
> **`docstore.pdfdoc` is NOT dropped.** The exit below says it is; the drop was moved to cutover instead —
> see `docs/MIGRATION-DATA-CHECKS.md`. The bytes are safely on disk, so the column is redundant rather than
> load-bearing, but dropping it is irreversible and belongs with the other cutover steps. A8 and C3 are
> closed; **C4's bloat reclamation is pending that drop.**

- **`IDocumentStorage` + a local-filesystem backend** writing **outside the web root** under
  **server-generated names**, with an **extension + content-type whitelist** and a **size cap**; a new
  `documents` metadata table (no bytes), streamed downloads (never re-materialised under the web root).
  Optional entity attachment (`entity_type`/`entity_id`).
- **Upload / list / download / soft-delete** endpoints (permission-gated), server-side validation as the
  authority.
- **The legacy-BLOB migration, then drop the BLOB column** (your decision): a one-off tool reads each
  `docstore.pdfdoc`, writes the bytes to the new store under a generated name, and creates a `documents` row —
  idempotent, dry-run first — and once the materialised count is verified against the row count, a migration
  **drops `docstore.pdfdoc`**, reclaiming the C4 bloat. The `docstore` metadata rows stay (read-only, Legacy
  Archive); no document is lost — each is on disk before the column goes.
- **Web:** a documents screen (upload with the whitelist enforced, list, download, remove) and an attach-to-record
  affordance where it makes sense.

**Exit:** a file uploads to disk outside the web root under a safe name (a bad extension/oversize is refused),
downloads by streaming, the existing legacy BLOBs are materialised into the store, and ~~`docstore.pdfdoc` is
dropped~~. A8 and C3 closed; **C4's bloat reclamation is pending the column drop, which moved to cutover** —
see the banner above and MIGRATION-DATA-CHECKS.md.

---

## Slice 5 — Personal notes · ~0.3 week

> **Built and shipped**, after one false start — see below. New **`user_notes`** table
> (`Phase7UserNotes` migration): `title` + `body`, **private to the author**, audit columns, soft-delete
> filter. `NotesController` (list/get/create/edit/reason-gated soft-delete) behind the existing legacy
> `notes` permission, with ownership enforced on every read and write. A standalone **`/notes` screen**
> — list, new/edit popup, per-note History dialog — plus a sidebar entry under Documents. Tests green
> (663); migration applied to the dev DB.

- **A `user_notes` table** (`title, body`, audit) + `NotesController` and a standalone **Notes screen**
  replacing the legacy one. Timestamped and attributed via the audit spine.

### What the legacy screen actually was

Worth recording, because it explains both the 49 legacy rows and the shape of the replacement.
`NotesController.NotesIndex` rendered **one textarea and a Save button**. `getNote` returned
`SELECT note FROM notes ORDER BY id DESC LIMIT 1` — only the newest row — and `saveNote` **INSERTed a
whole new row** on every save. So it was a single shared scratchpad, and its 49 rows are 49 full
snapshots of the same growing list (which is why lengths climb 22 → 940 characters and checksums
repeat). No titles, no list, no editing, no history, and no privacy.

### Decisions taken while building it

- **The first cut was wrong and was rebuilt.** It was first built as a *per-entity panel* bolted onto the
  invoice, job-card and customer views — reading this plan's "per-entity notes" literally instead of
  checking what the legacy screen did or what was actually wanted. The requirement was a standalone
  module with a title, a list, a popup, edit and history. The panel was removed and the model rebuilt as
  `UserNote`. Recorded because the plan's own wording is what misled the build.

- **Notes are private to their author, not company-scoped.** `created_by` is the visibility rule; the
  `notes` permission decides who may use the feature at all. Two independent checks. `company_id` is
  recorded but deliberately **not** filtered on — switching company must not hide a personal note, and a
  test pins that.

- **A note that is not yours is a 404, not a 403.** A 403 would confirm the note exists.

- **The table is `user_notes`, the entity `UserNote` — because `notes` was already taken.** The legacy
  table owns that name (49 rows) and the first migration failed on dev with *"Table 'notes' already
  exists"*. `LegacyTableNameTests` now pins the set of table names the new context shares with the legacy
  schema, so this class of collision fails the build rather than the migration.

- **Nothing migrates the 49 legacy rows.** They are one shared list with no author and no per-note
  structure. See MIGRATION-DATA-CHECKS.md — which also records that they are **plaintext credentials**
  and should not be carried into the Legacy Archive as they stand.

**Exit:** a note is created from the popup with a title and body, appears in the list, is edited in place,
carries its own audit history, and is invisible to every other user.

---

## Slice 6 — The acceptance test, reconciliation & E2E · ~0.3 week

> **Built and shipped — Phase 7 is complete.**
>
> **The acceptance test was already there.** Slice 1 delivered it
> (`A_receipt_allocated_across_two_invoices_settles_both_and_dual_writes_the_legacy_shadow`), with idempotency
> and over-allocation as their own tests. One part of the claim was *not* covered, and is now:
> `A_receipt_that_fails_part_way_leaves_no_trace_of_the_allocations_that_had_succeeded` — a valid allocation
> followed by an invalid one, asserting the first leaves no ledger entry, no legacy row and no moved balance.
> That is the transactional half, and it is the defect the design exists to close (B2: the legacy `savePay`
> issued an `INSERT` and an `UPDATE` with nothing joining them).
>
> **Reconciliation** appended to `legacy-analysis/RECONCILIATION.md`: 2,187 invoices carrying a payment,
> **2,185 exact (99.91%)**, no sub-penny residue at all — the balance either agrees with its payments or it
> does not. The two that fail are **STI-38 and SNI-915**, found again from a completely different direction:
> this test never looks at dates or duplication, only at whether the balance agrees, and it lands on exactly
> the two overpayments the same-date remediation missed.
>
> **`phase7.spec.ts`** covers the exit case end to end (two invoices → one receipt split 50/30 → both balances
> fall to 68 and 88), plus a receipt visible on the invoice it settled, a cheque and an expense.
> **`POST /api/dev/seed-payment` is deleted** and `invoice.spec.ts` now takes its payment through the real
> receipts screen. `npm run e2e` green: **8 passed**.
>
> **The harness was broken before any of this, and had been since 16 July.** `E2EHost` runs `--no-build`, its
> binary was four days stale, and it therefore built an *older migration set* — so the schema silently lagged
> the code and every save 500'd on `Unknown column 'c.business_registration_no'`. All 8 tests failed, including
> the 4 nobody had touched. `npm run e2e` now builds both projects first (`e2e:build`), and the trap is
> documented at the `--no-build` call. Three assertions in the untouched specs had also drifted from the UI
> and were only masked by the breakage: an ambiguous `Pending`, a job-card URL that now carries `?print=1`,
> and a close-job dialog whose field is "Completion remarks", not "Reason".

- **The acceptance test, automated** (slice 1's exit): a receipt across two invoices → both derived balances
  fall, legacy `payments` + `invoice_h.balance` in step, a double-submit is idempotent. Unit + integration.
- **Reconciliation.** A sample of legacy `payments` recomputed against the invoices they settled (does the
  stored balance match the invoice total minus its payments?), appended to
  [legacy-analysis/RECONCILIATION.md](legacy-analysis/RECONCILIATION.md) — the duplicate-payment groups
  (Finding 1) surface here as Data-Exceptions candidates, left as-is per decision 7.
- **The Phase 7 E2E** on the ephemeral-MariaDB harness: login → raise two invoices for a customer → **take one
  receipt allocated across both** and see both derived balances fall → record a cheque and an expense. This
  **retires `POST /api/dev/seed-payment`** — the E2E now takes payments through the real endpoint. `npm run
  e2e`, green.

**Exit (the phase exit):** the receipt-across-two-invoices case passes end to end, the reconciliation sample is
recorded, and the payments E2E uses the real path.

---

## What Phase 7 does NOT do

- **No cheque or document PDF printing / styled templates** — the cheque overlay (pixel-exact, pre-printed
  stationery) and the six QuestPDF templates are **Phase 8**. The register carries `printeddt`; it does not
  produce the print.
- **No `varchar → DECIMAL`/`DATE` retype of the legacy columns** — new rows write new typed columns and shadow
  the legacy `varchar`; the retype is **Phase 9**.
- **No deletion of legacy data — with one confirmed exception.** The legacy `payments`/`cheques`/`expense_tr`
  rows and the trivial legacy `Note` table are left in place (LEGACY-DATA-POLICY). The exception: after every
  `docstore` BLOB is materialised to a file, the redundant **`docstore.pdfdoc` column is dropped** (your
  decision) — the document itself is preserved (on disk + in `documents`), only the in-DB copy goes; the
  `docstore` metadata rows remain. **The drop did not happen in this phase** — it moved to cutover,
  because it is the one irreversible step. Dev is materialised and verified; live is not.
- **No object storage yet** — documents go to the local filesystem behind `IDocumentStorage`; the S3/MinIO swap
  is a later config change, not a rewrite.
- **No supplier/customer double-entry accounting** — expenses are a flat log; cheques are a register; neither
  posts to a ledger. Only customer payments touch the (receivables) ledger.

---

## Carried forward

| Item | Why it matters here |
|---|---|
| **Customer payments dual-write the legacy shadow** | The ledger owns the truth; a legacy `payments` row + `invoice_h.balance` are dual-written per allocation so `PaidAfterAsync` / the outstanding report and any `invoice_h.balance` reader stay correct — "not ledger-only". |
| **Allocation across invoices** | One receipt → many allocations → many ledger `Payment` entries + many legacy `payments` rows, so legacy detail reads the same while the new model is richer than one-to-one. |
| **Idempotency closes Finding 1** | The legacy `savePay` had none (the double-click duplicate mechanism); the receipt carries an idempotency key and the save is transactional. |
| **Legacy readers of these tables** | `ChequeReport`, `ExpenseReport` and the outstanding report read `cheques`/`expense_tr`/`payments` live — every new write dual-writes until Phase 9. |
| **The two Phase 5 stopgaps retire here** | The `data_origin='legacy'` scoping of the legacy payment screen, and `POST /api/dev/seed-payment` — both replaced by the real payments module/endpoint. |
| **Legacy BLOBs → filesystem, then drop the column** | Documents are materialised out of `docstore.pdfdoc` into the new store, then the redundant `docstore.pdfdoc` column is dropped (confirmed exception) — bytes safe on disk, DB bloat reclaimed. A8/C4 closed. |
| **`company_id` already present** | On `cheques`/`expense_tr`/`payments` (multi-company migration) but not yet mapped on the entities — the adoptions map it and add `data_origin`. |
