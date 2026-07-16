# Phase 5 — Documents engine

~5 weeks. Quotations, invoices, credit notes — the module where the legacy app's
*four-controllers-per-document* sprawl collapses into one engine, the server-session "cart" is
deleted, and money becomes `decimal` behind a real tax engine and a derived ledger. This is **the
bulk and the risk** of the whole migration: it is the first phase that *writes* financial documents,
and it is where most of the ISSUES money defects are closed at once (B2, B3, B4, B5, B6, D2, D4).

Parent: [DEVELOPMENT-PLAN.md](DEVELOPMENT-PLAN.md) · Previous: [PHASE-4-PLAN.md](PHASE-4-PLAN.md)

**Exit criterion (adapted from the parent plan):** an invoice with the company's VAT rate, a discount
and a partial payment produces the correct total, a correct ledger, and a correct audit record — proven
by tests. *(The parent plan wrote "mixed VAT rates"; the business confirmed 2026-07-15 that documents
use a single company rate, so the case exercises that rate rather than a mix — the discount, the ledger
and the partial payment are what it is really testing.)* That single case covers most of what the
legacy system got wrong; if it does not pass, the engine (slice 1) is not finished.

---

## What must already be true before slice 1

Phase 5 does not start from nothing — the scaffolding is unusually complete, and the plan depends on
it. Confirmed present (Phases 1–4):

- **The transactional number allocator exists.** `DocumentNumberAllocator.AllocateAsync` already does
  `SELECT … FROM document_series … FOR UPDATE`, increments, and **throws unless it is called inside a
  transaction**. It is DI-registered and has **no caller** — slice 1 is its first. `document_series`
  carries a **template** prefix (`{YY}{MON}_SNIN_` rendered at the document's date — Finding 11), so
  August produces `26AUG_` without an edit. Numbering is *initialised* per company via
  `NumberSeriesInitialiser` (reads the legacy sequence tables, takes the max, never moves backwards).
- **The version-snapshot machinery exists and waits for a writer.** `document_versions` (table,
  entity, EF config), `AuditHistoryReader.VersionsAsync/VersionAsync`, and the web `History` tab all
  exist; the only thing missing is a service that writes **version 1 at creation**. Nothing writes to
  it today (a repo search finds writes only in tests).
- **Tax rates are stored, per-company and effective-dated.** `TaxRate` (`decimal` percentage,
  `EffectiveFrom`/`EffectiveTo`, `IsDefault`, per `CompanyId`) with CRUD in settings. `Company` carries
  `IsVatRegistered` + `VatNumber`. **There is no tax *engine*** — nothing resolves a rate for a date,
  snapshots it, or honours `IsVatRegistered`. That is slice 1.
- **The audit spine is automatic.** The `SaveChanges` interceptor writes `audit_log` inside the same
  transaction; `X-Change-Reason` is enforced server-side by `RequireChangeReasonAttribute`;
  `row_version` optimistic concurrency is on every entity. Documents inherit all of it for free.
- **The master data the documents reference is adopted.** `Customer` (surrogate `Id`, `Code`,
  `CreditLimit` as `decimal`, `AssignedCompanyId` as a *default* not a filter), `Item` (`Id`, `Code`,
  `SellingPrice`, `Cost`, no per-item tax by design), the immutable `StockMovement` ledger (with
  `Issue` **reserved for Phase 5**), `Company` (full document-header fields). The `StockBatch.Balance`
  legacy column is read-only to the new app — stock is derived from `StockMovement`.
- **The client draft editor is prototyped and its payload contract is settled.** `components/line-items`
  holds the browser-side draft (item vs service, keyboard-driven, `localStorage`-persisted), and
  `draft.ts`'s `toPayload` is the intended body of `POST /api/invoices`: per-line `quantityThousandths`,
  `unitPriceMinor`, `discountPercent`, **per-line `taxRate`**, and a **nullable `itemCode`** (the fix
  for the legacy "item invoice throws the item code away" bug). It saves nothing yet.

**Not a prerequisite, deliberately:** *legacy data remediation.* Per
[LEGACY-DATA-POLICY.md](LEGACY-DATA-POLICY.md) (decision 7) the 1.55M of duplicate payments, the 45
negative balances and the 4 broken totals are **left as-is** and imported as opening balances — they
are **not** fixed before Phase 5. The `varchar → DECIMAL` / `varchar → DATE` retype likewise **stays
in Phase 9**; new documents write **new `decimal`/`date` columns additively** and dual-write the legacy
`varchar` columns so the still-live legacy app keeps reading. The earlier suggestion of a separate
"remediation phase before Phase 5" (in DATA-AUDIT-FINDINGS) was superseded by that decision.

---

## What the legacy source says before we plan anything

Read against the live controllers at `C:\Users\Saboor.a\Desktop\SMARTNET_DEV\Smart_InvSys\Controllers`,
not assumed. Eight document controllers (plus edit/search variants) share the same habits, and the
engine exists to end them all at once:

1. **Four controllers per document, split on two axes** — *create vs edit vs search*, and *Item vs
   Service*. `InvoiceController` (item) and `ServiceInvoiceController` (service) write the **same**
   `invoice_h`/`invoice_l`, discriminated only by the header column `it = 'ITEM' | 'SERVICE'`; the pair
   is ~90% duplicated code. The only real differences: an Item line carries a stock id and a `Cost` and
   **decrements `item_stock`**; a Service line does neither. Quotations mirror this exactly
   (`quotation_h.it`), credit notes are a third pair. One engine, one discriminator, collapses all of it.
2. **The line list lives in server `Session`.** `addtoCart`/`removeQItem`/`*cartLoad` mutate a
   `List<T>` in `Session[...]` — one AJAX round-trip *per line operation* (~10 endpoints per document),
   no line-edit action (remove and re-add), the running total re-`Sum()`-ed and sent to the client for
   display, and **nothing persisted until Save**. If the session drops, the draft is gone. This is D4,
   and killing it is the single biggest behavioural change in the migration — the browser already holds
   the draft in the prototype.
3. **Money is `double`, and the one document rate is resolved by the wrong date.** Line
   `tot = rate*qty` (a parsed string), one **document-level discount %**, one **document-level VAT %**
   applied once to the discounted subtotal. The single-rate-per-document model is **correct for this
   business** (no mixed rates — confirmed 2026-07-15), so the new engine keeps it; what it fixes is the
   `double` math and that the rate comes from the *company's* `vatcode` against `vat_validity` **by
   `CURDATE()`** — so re-printing an old invoice picks up today's rate, not the rate it was issued under
   (B5/B6). Several legacy paths also compute VAT on the pre-discount subtotal, disagreeing with each
   other (a latent bug the one engine ends).
4. **Numbering is a non-transactional sequence insert.** `INSERT INTO invoice_seq …` then read
   `LAST_INSERT_ID()` — separate, non-transactional round-trips from the header insert. The number
   itself won't collide (auto-increment), but a failed header insert **burns the number** (a gap) with
   no rollback, and there is no unique index behind it — which is exactly how **two quotations both got
   numbered `STQ-0`** (Finding 9, B4).
5. **Balance is a mutable string, and every save is many un-transactioned round-trips.** A 5-line cash
   invoice is ~14 auto-committed statements with stock decremented *between* line inserts; a failure
   midway leaves a half-written document (B2). `invoice_h.balance` is set to `0` (cash, plus an
   auto-inserted `payments` row) or `totamount` (credit), then later mutated in place —
   `UPDATE invoice_h SET balance = balance - x` — the exact mechanism that produced the duplicate
   payments (B3). **Editing** an invoice *deletes and re-inserts* its lines, **resets `balance`**
   (wiping partial-payment history) and, for cash, **inserts a second `payments` row** (double-books).
   There is **no versioning or audit** of an edit. `delCN` is outright **broken** in the live code
   (wrong column names) and throws.
6. **Quote → invoice is re-runnable and unlinked.** `convertItemInvoice`/`convertSerInvoice` copy a
   quote's lines into a new invoice but **never mark the quote converted** and store **no back-link** —
   the same quote can be converted repeatedly, decrementing stock each time.

All ten legacy money paths are the same shape, and slice 1 is where that shape is replaced once.

---

## Slice 0 — decisions taken before any code

Three settled, one flagged for sign-off.

**A. New documents live in the *adopted* legacy tables, additively — not new tables.** `invoice_h/l`,
`quotation_h/l`, `cn_h/l` are adopted onto the new side exactly as the master-data tables were: a
surrogate `id` PK, new-side columns (`decimal` money, per-line tax snapshot, `company_id`, a
`prepared_by` **user id**, `data_origin = 'new'`, a nullable FK from line → header surrogate id), and
the **legacy `varchar` columns kept populated on write** so the still-live legacy app (its reports,
its cross-references) keeps reading new documents. This is the strangler rule — *every legacy read must
still work* — and it is the reason new tables are the wrong choice: the legacy app reads `invoice_h`,
and it will until Phase 6/7 port the modules that do. The `varchar → DECIMAL` retype of the *legacy*
columns still waits for Phase 9; until then the new `decimal` column is the source of truth for new
rows and the `varchar` column is a compatibility shadow.

**Confirmed against the legacy source (2026-07-15), so it is no longer an assumption:** after Phase 5
removes the invoice/quote/CN create screens, the surviving readers of `invoice_h` are (a) **the new
app's own Phase 4 reports and dashboard**, which read it live through `SmartnetLegacyDbContext`, and
(b) the **not-yet-ported legacy modules** — `CusPaymentsController` (payments, Phase 7) and
`JobCardController` (job cards, Phase 6). `quotation_h` keeps only one non-replaced reader
(`CustomerController`, a customer's quote history); `cn_h` keeps **none**. The columns those consumers
actually read give the **dual-write set** a new invoice must keep populated on `invoice_h`:
`invoiceno, customer, company, it, invtype, indate/invdate, totamount, balance, novattotal, cost,
vtype, vper, preparedby, pono` (and the `invoice_l` line columns `inno, itemcode, desc, qty, rate,
tot`). This is not a token subset — the new save writes the whole legacy row alongside its `decimal`
columns until the Phase 9 retype.

> ⚠️ **The hazard that same read surfaced — state it, do not discover it in production.**
> `CusPaymentsController` (still the *only* payment screen until Phase 7) does
> `UPDATE invoice_h SET balance = balance - amount` **directly**, and it filters by nothing — so it can
> see the **new** invoices adopted into `invoice_h` and mutate their `balance`, silently diverging from
> the new derived ledger that is supposed to own it. Three ways to close it, to be decided in slice 1:
> (1) scope the legacy payments screen to `data_origin = 'legacy'` (a small, backed-up legacy edit —
> the cleanest); (2) bring customer-payment *taking* forward so new invoices are only ever paid through
> the new ledger (Phase 5 already records payments for its own exit criterion); or (3) treat `balance`
> on a `data_origin='new'` row as new-app-owned and reconcile. **Chosen (2026-07-15): option 1.**

**B. Item and Service are one document with one discriminator.** There is no "item invoice" and
"service invoice" — there is an invoice whose *lines* each either reference an `Item` (surrogate
`ItemId`, carries a cost, **posts a `StockMovement.Issue`**) or are free-text service lines (no item,
no stock). `DraftLine.itemCode` nullable already encodes this. One controller, one save pipeline, one
engine per document type — collapsing 8 legacy controllers to 3.

**C. The ledger is born here; the payments *module* is Phase 7.** Balances derive from a ledger, never
a mutated column (B3). Phase 5 builds the receivables ledger and posts to it the entries that *are*
part of issuing a document: the **invoice charge**, the **credit-note credit**, the **cash-at-issue
settlement** (a cash invoice is settled when issued — the legacy auto-payment, done correctly), and the
**`OPENING_BALANCE`** import for every legacy invoice (the stored balance, verbatim, per
LEGACY-DATA-POLICY §2). It also supports **recording a payment against an invoice** — the minimum the
exit criterion's "partial payment" requires. The *management* surface — the payments search/allocation
screens, the cheque register, expenses — is **Phase 7**. Phase 5 proves the ledger; Phase 7 furnishes
the room.

**D. Four money/rate decisions — settled 2026-07-15.**

- **One rate per document — the selected company's — applied to every line. No mixed rates**
  (confirmed by the business 2026-07-15; see the `one-vat-rate-per-document` decision). The engine
  resolves the company's **default `TaxRate` effective at the document's date** (or 0 when the company
  is not VAT-registered) and **snapshots that percentage onto the document** at save, so a reprint
  reproduces it. This drops the prototype's per-line `taxRate` and the VAT-grouped-by-rate summary — a
  line carries no rate; the document does. It matches the legacy company-driven model, fixed only where
  it was wrong (resolved by document date not `CURDATE()`, `decimal`, transactional).
- **Rounding is per line** (the `app_settings` toggle's default): round each line's tax, then sum — so
  the printed line figures re-sum to the document total, matching the prototype's VAT-grouped summary.
- **A cash invoice books a real settling `PAYMENT` ledger entry at issue**, alongside its `CHARGE` — the
  legacy auto-payment, done correctly. "Paid" is a derived fact (`Σ ledger = 0`), never a flag.
- **The legacy-payments balance hazard (slice 0-A) is closed by scoping the legacy `CusPayments` screen
  to `data_origin = 'legacy'`** — a small, backed-up legacy edit — so it can never touch a new invoice's
  balance. New invoices are paid only through the new ledger.

---

## Slice 1 — The document engine · ~1 week · *the spine*

Nothing else is built first. Get this right once and three document types inherit it; get it wrong and
each re-implements a tax parser. Proven end-to-end by **invoices** (slice 2 is then a thin skin on it).

- **The tax engine — `decimal`, one rate per document, snapshotted, date-correct.** In `Smartnet.Domain`
  (no EF, no HTTP). It resolves the company's **default `TaxRate` effective on the document's date** (not
  `CURDATE()` — B5/B6), applies it to every line, and computes line net/discount/tax and the totals. The
  resolved percentage is **snapshotted onto the document at save and never re-resolved** (a reprint
  reproduces the issued figures). It honours `Company.IsVatRegistered` (a non-registered entity — Smart
  Technologies — issues zero-VAT documents). Rounding per `app_settings`, per-line by default (slice
  0-D). **No mixed rates** (the business does not use them — 2026-07-15). This is the code the browser
  prototype's totals mirror; the **server is the authority** and its result is what persists.
  *(Built and unit-tested — 7 cases green: the discounted invoice, discount-before-VAT, non-registered →
  0, date-based resolution, no-rate-in-force rejected, per-line vs per-document rounding.)*
- **The transactional save pipeline.** One method, one transaction, for any document:
  `BeginTransaction → AllocateAsync (FOR UPDATE) → insert header + lines (decimal + legacy shadow) →
  post ledger entries → post StockMovement.Issue for item lines → write version-1 snapshot → commit`.
  Either all of it commits or none (B2). The number is allocated **inside** the transaction, so a failed
  insert rolls the counter back — no burned gaps (B4). An **idempotency key** on the request stops a
  double-clicked Save creating a second document.
- **The version-1 snapshot writer.** The missing writer for `document_versions`: at creation it writes a
  self-contained JSON snapshot (header + lines + **resolved tax** + company header as they stood), so
  the History tab and reprint work from day one. Version 1 is written at *creation*, not first edit.
- **The document aggregate + the shared API shape.** New-side `Invoice`/`InvoiceLine` entities (then
  `Quotation`, `CreditNote` reuse the shape), EF config, an additive migration adopting `invoice_h/l`,
  a `DbSet`, contracts/validators, and `POST /api/invoices` whose body **is** the prototype's
  `toPayload`. Server-side validation via FluentValidation (customer exists, item exists or line is
  service, quantities positive, discount 0–100).
- **The ledger.** A receivables ledger table (append-only, audited) with entry types
  `CHARGE | CREDIT | PAYMENT | OPENING_BALANCE`, each tied to a document and a customer; a customer's
  balance is `Σ entries`, never a stored column. A one-off importer seeds `OPENING_BALANCE` from the
  legacy `invoice_h.balance` (verbatim, including negatives — LEGACY-DATA-POLICY §2).

**Exit:** a POST creates an invoice — the company rate applied to every line, a discount — with correct
`decimal` totals, a ledger charge, a stock issue, an audit row and a version-1 snapshot, all in one
transaction, all proven by unit + integration tests. Slice 2 adds no engine.

---

## Slice 2 — Invoices · ~1 week

The engine, given a real skin — item and service collapsed (slice 0-B).

- **The create screen** = the prototype editor, wired: the free-text customer becomes the `/api/customers`
  picker (carrying the customer surrogate id, not a code string), the hardcoded catalogue becomes the
  `/api/items` picker (carrying `ItemId`, cost and `SellingPrice`), and Save POSTs the whole draft.
- **Credit limit, enforced server-side.** The legacy check is a client-side advisory that a direct POST
  bypasses; here `Customer.CreditLimit` is enforced **before the save** (outstanding ledger balance +
  this invoice vs the limit; 0 = no limit), per the credit-limit **setting** (enforcement on/off).
  **Applies to both cash and credit invoices** (confirmed 2026-07-15) — the limit gates the sale, not
  just the credit terms; the legacy check ran on service invoices only.
- **Cash vs credit.** Credit → a `CHARGE` for the full amount. Cash → a `CHARGE` and the cash-at-issue
  `PAYMENT` (slice 0-C), so the invoice is settled with a real ledger entry, not a magic `balance = 0`.
- **Stock issue.** Each item line posts a `StockMovement.Issue` (signed, immutable) inside the
  transaction — the legacy interleaved `UPDATE item_stock SET balance = balance - qty` becomes one
  append to the ledger the new app already owns.
- **Search & view.** The invoice list (the shared `DataTable`), a read view, the History tab
  (versions + audit), and a simple print (styled PDF templates are **Phase 8** — here it is the plain
  document view).

**Exit:** a salesperson creates a mixed item/service invoice from the browser draft, over the real
customer and item pickers, with the credit limit enforced and stock issued — with zero server round
trips while typing and one at Save. The legacy item + service invoice screens leave their menu.

---

## Slice 3 — Quotations & conversion · ~0.75 week

The same engine with **no stock and no ledger impact** — a quotation charges nothing and issues nothing.

- **Quotation create/edit/search** on the shared engine and editor; `quotation_h.it` collapses the same
  way invoices did.
- **Quote → invoice conversion, done correctly.** Convert builds an invoice through the *same save
  pipeline* (so it gets a number, a ledger charge, stock issue, a snapshot — none of which the legacy
  copy-paste conversion did), **marks the quotation converted with a back-link to the invoice**, and
  **refuses a second conversion** — closing the re-runnable-conversion / double-stock bug. The two
  legacy VAT-branch inconsistencies are reconciled by there being one engine.
- **STQ-0 stays as-is.** The duplicate-`q_no` pair (Finding 9) is **not** renumbered — it surfaces in
  Data Exceptions; the unique index on `quotation_h` is applied once the business resolves it. New
  quotations get transactional numbers and cannot collide.

**Exit:** a quotation converts to an invoice exactly once, the invoice carries a link back to its quote,
and the quote shows as converted. A second conversion attempt is refused.

*(Built and tested — quotation adoption (`quotation_h`/`quotation_l`, additive migration), the
no-ledger/no-stock `QuotationCreator`, and the `QuotationConverter` that builds the invoice through the
shared `IInvoiceCreator.CreateInCurrentTransactionAsync` inside one transaction, links both documents,
and refuses a second conversion. Three integration tests green: create touches no ledger or stock,
convert-once raises a real invoice with a charge + stock issue + back-link, second convert refused. Web:
quotation list/new/view on the shared `LineDraftEditor`, with a convert dialog. All 350 API tests pass.)*

---

## Slice 4 — Credit notes · ~0.75 week

A credit note against an issued invoice — the mirror of an invoice, posting the opposite ledger sign.

- **Create against a parent invoice**, on the shared engine. It posts a `CREDIT` ledger entry (reducing
  the customer's balance through the ledger, never `UPDATE invoice_h SET balance = balance - x`), and,
  where the note returns goods, a `StockMovement` **receipt** back into stock.
- **The broken legacy delete is not ported.** `delCN` throws today (wrong column names); the new delete
  is the soft, versioned, reason-gated delete of slice 5.

**Exit:** a credit note against an invoice reduces that customer's derived balance by the note's amount
through a ledger entry, returns any goods to stock, and reprints as it was issued.

*(Built and tested — credit-note adoption (`cn_h`/`cn_l`, additive migration), the `CreditNoteCreator`
that posts a `CREDIT` ledger entry (opposite sign, tied to the parent invoice) and, when the note returns
goods, a `StockMovement.Receipt`, all in one transaction. The note's VAT is **inherited from the parent
invoice's snapshot** — a small optional rate-override on the shared `TaxEngine` — so a full credit nets
exactly against the invoice and crediting an old legacy invoice never depends on the rate table still
covering its date. The controller resolves the parent (new **or** legacy invoice) to the customer, company
and rate. Three integration tests green: a full credit nets the ledger to zero with an issue+receipt that
net to zero stock; a no-stock credit leaves stock untouched; a legacy parent's null rate-id is carried
with the inherited rate. Web: credit-note list/new/view on the shared `LineDraftEditor`, the new screen
seeding its lines from a picked invoice. All 363 API tests pass.)*

---

## Slice 5 — Edit, version & delete · ~1 week

Where the legacy app is most dangerous, and where decision 3 (invoices stay editable) is made
defensible.

- **A versioned, reason-gated edit.** Editing an issued invoice requires `X-Change-Reason` (min 10
  chars, per AUDIT.md §5), writes a **new `document_versions` snapshot**, re-runs the tax engine, and is
  guarded by `row_version` so two editors conflict loudly instead of one silently overwriting the other.
  It does **not** delete-and-reinsert lines destructively, does **not** reset the balance (the ledger is
  untouched except by explicit adjustment), and does **not** double-book a cash payment — the three
  legacy edit bugs, gone.
- **Soft, recoverable, attributable delete.** Nothing is hard-deleted (a soft delete via the interceptor);
  the deleted-invoice audit list replaces `DeletedInvoicesController`. Delete requires a reason; deleting
  a document reverses its ledger and stock entries through *new* entries, never by erasing history.
- **The History tab, live.** With versions now being written, the tab shipped in Phase 2 shows the
  version list, the side-by-side diff, print-this-version, and permission-gated restore — for real,
  for the first time.

**Exit:** an issued invoice is edited with a reason, the change is a new version with a diff, the prior
version still prints as it was, a concurrent edit is rejected, and a delete is recoverable and audited.

*(Built and tested. **Edit** (`IInvoiceEditor`): re-runs the tax engine at the invoice's **snapshotted**
rate — via the same `TaxRateOverride` credit notes use, so an edit corrects figures without re-rating —
reconciles lines **in place** by id (update / add / soft-delete, never delete-and-reinsert), writes a new
`document_versions` snapshot with the reason, and is `row_version`-guarded (a concurrent edit → 409). A
changed total adjusts the ledger by a single compensating `CHARGE` delta — the balance is never reset.
**Two business rules confirmed 2026-07-16:** a **paid** invoice cannot be edited — any `PAYMENT` entry (a
cash invoice's settlement included) refuses the edit until the payment is deleted; and an **item invoice's
stock is adjusted automatically** — the reconcile nets each item's issued quantity, so an increased line
issues the extra units and a reduced or removed line returns them. **Delete/void** (`IInvoiceDeleter`):
reason-gated, `row_version`-guarded, soft (nothing hard-deleted — `deleted_at` set directly so the
FK-nulling cascade of `Remove()` cannot wipe the reversal entries); the ledger is reversed to zero through
one compensating entry and stock returned via a receipt. The deleted-invoice register
(`GET /api/invoices/deleted`, reason from the audit trail) replaces `DeletedInvoicesController`. Web: Edit
and Void on the invoice view, the edit screen on the shared `LineDraftEditor` (line ids round-tripped
through the draft key), the History tab live, and the deleted register. Nine integration tests green; full
suite 378.)*

---

## Slice 6 — The non-negotiable test & reconciliation · ~0.5 week

- **The acceptance test, automated.** One invoice at **the company's VAT rate, with a discount and a
  partial payment** → correct `decimal` total, correct ledger (charge minus partial payment =
  outstanding), correct audit and version records. Unit (tax engine, ledger, numbering, credit limit) +
  integration (the endpoint against a throwaway MariaDB) + a Playwright E2E (login → create invoice →
  take a partial payment → check the derived balance).
- **Reconciliation against legacy.** Recompute a sample of existing invoices through the new tax engine
  and compare to the stored legacy figures; **expect small differences** (binary `double` vs `decimal`)
  and record the policy for them. This is the risk the parent plan names under "Money correctness."

**Exit (the phase exit):** the company-rate/discount/partial-payment case passes end to end, and a
sample reconciliation is signed off.

---

## What Phase 5 does NOT do

- **No payments-management module.** Recording a payment against an invoice exists (the ledger + the
  exit criterion need it); the payments *search/allocation* screens, the **cheque register** and
  **expenses** are **Phase 7**.
- **No purchase orders, supplier invoices or job cards** — same engine, **Phase 6**.
- **No styled PDF templates.** Documents print from a plain view; the six QuestPDF templates are
  **Phase 8** (the cheque especially — pixel-exact stationery).
- **No legacy data remediation.** The duplicate payments, negative balances and broken totals import as
  `OPENING_BALANCE` and surface in **Data Exceptions** (LEGACY-DATA-POLICY); Phase 5 does not touch them.
- **No `varchar → DECIMAL` retype of legacy columns.** New rows write new `decimal` columns and shadow
  the legacy `varchar`; the retype is **Phase 9**.
- **No customer portal.** Dropped (decision 2, Finding 7).

---

## Carried forward

| Item | Why it matters here |
|---|---|
| **Dual-write column set (confirmed)** | The surviving readers of `invoice_h` are the new Phase 4 reports/dashboard and the unported legacy payments (P7) and job cards (P6). New invoices dual-write the full legacy row (slice 0-A) until the Phase 9 retype. |
| **Legacy payments mutate `invoice_h.balance` directly** | `CusPaymentsController` (live until P7) can write the balance of a *new* invoice adopted into `invoice_h`, diverging from the new ledger. Close it in slice 1 — recommended: scope the legacy screen to `data_origin='legacy'`. |
| **Rounding mode + cash-at-issue settlement** | Two business statements (slice 0-D) that write money/ledger. Confirmed in slice 1, not assumed. |
| **Rs 1.55M duplicate payments (Finding 1)** | Import as `OPENING_BALANCE`, verbatim; the derived ledger starts from history, does not recompute it. Surfaced in Data Exceptions. |
| **STQ-0 duplicate quotation number (Finding 9)** | Not renumbered; the `quotation_h` unique index waits for the business to resolve it. New quotations number transactionally. |
| **`preparedby` name-string** | New documents carry a `prepared_by` **user id**; the dashboard "my view" and sales-by-user (Phase 4) stop joining on a name once documents write it. |
| **Invoice prefix is a template (Finding 11)** | `document_series` renders `{YY}{MON}_SNIN_` at the document's date; already built, first used here. |
| **The non-negotiable test** | Mixed VAT + discount + partial payment. If it does not pass, the engine is not done — it is the phase's whole justification. |
