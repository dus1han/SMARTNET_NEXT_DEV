# Phase 6 — Purchasing & service

~3 weeks. Purchase orders, supplier invoices and job cards — the three modules that turn the
*supply* side of the business into the same disciplined shape Phase 5 gave the *sell* side. Unlike
Phase 5, the engine already exists: Phase 6 is mostly **reuse**. It adds exactly one genuinely new
piece of infrastructure — a **payables ledger** — and one net-new capability the legacy app never
had — **structured, serial-tracked job-card lines**. It also retires the last `;`-separated master-data
string by giving customers **structured contacts**.

Parent: [DEVELOPMENT-PLAN.md](DEVELOPMENT-PLAN.md) · Previous: [PHASE-5-PLAN.md](PHASE-5-PLAN.md)

**Exit criterion:** a **purchase order** records an order (item + service lines) with correct `decimal`
totals and snapshotted tax; a **supplier invoice** records a payable that two **partial payments** settle
to a derived-zero balance (Pending → Paid as a *derived* fact, never a flag); and a **job card** with
**serial-tracked lines** moves PENDING → CLOSED capturing cost/sell — each versioned, audited, and
transactional, proven by tests. If that passes, the supply side has the same guarantees the sell side got
in Phase 5.

> **Scope note (2026-07-16):** the **GRN (Goods Received Note)** — which receives a PO's goods into stock
> in *partial* quantities over time, because a PO's full quantity rarely arrives at once — is **deferred
> to a later phase**. Consequently a Phase 6 PO **posts no stock**; it records the order. Stock continues
> to enter through the **existing Phase 3 Item Stock adjustment** until the GRN is built. No Phase 6
> document moves stock.

---

## What must already be true before slice 1

Phase 6 starts from an unusually complete engine. Confirmed present (Phases 1–5, read against the
source 2026-07-16):

- **The document doc-types are already declared.** `DocumentTypes` (a static class of string
  constants, not an enum — `Smartnet.Domain/Settings/DocumentSeries.cs`) already carries
  `PurchaseOrder = "PO"`, `SupplierInvoice = "SUPINV"`, `JobCard = "JOBCARD"`. The number allocator
  accepts any *known* doc-type; Phase 6 only needs a `document_series` **row** per company for each.
- **The transactional save pipeline is proven and reusable.** `IInvoiceCreator.CreateInCurrentTransactionAsync`
  (`Smartnet.Infrastructure/Documents/InvoiceCreator.cs`) is the canonical shape — *guard-in-transaction
  → tax calc → allocate number (FOR UPDATE) → build header+lines (decimal + legacy shadow) → post ledger
  → post stock → version-1 snapshot*. `QuotationCreator` (no ledger, no stock) and `CreditNoteCreator`
  (opposite-sign ledger, `StockMovement.Receipt`) already demonstrate the two variations Phase 6 needs.
  `QuotationConverter` shows how to compose one document's save inside another's transaction.
- **`StockMovement.Receipt` already exists** (`Smartnet.Domain/MasterData/StockMovement.cs`, enum value
  earmarked "Phase 6") and is already posted by `CreditNoteCreator.PostStockReceipts`. The deferred GRN
  will receipt a PO's goods by the identical append — `{Type = Receipt, Quantity = +qty}` — but **no
  Phase 6 document posts stock** (the GRN is later; the PO records only the order). Balance stays derived
  (Σ movements), never stored.
- **The document-adoption template is settled.** The hand-written additive migration
  `20260715133011_Phase5AdoptInvoices.cs` (+ its quotation/CN siblings) and `DocumentConfiguration.cs`
  are the copy-paste pattern: `ALTER TABLE ... ADD COLUMN` (nullable / `NOT NULL DEFAULT`), a surrogate
  `id` PK where the legacy table had none, a **nullable** line→header FK, `data_origin` defaulting to
  `'legacy'`, the seven audit columns, a `*LegacyShadow` tuple list, and a `data_origin='new'` query
  filter. Never a `DropTable`; `Down()` drops only what `Up()` added.
- **The supplier master is in place.** `Supplier` (surrogate `Id`, `Code`) and `ISupplierCodeAllocator`
  exist from Phase 3. Only the supplier-side *ledger* is missing.
- **Audit, versioning and concurrency are automatic.** The `SaveChanges` interceptor stamps and diffs
  every `IAuditable` into `audit_log` in the same transaction; `IDocumentVersionWriter.WriteAsync` writes
  a self-contained v1 snapshot inside the caller's transaction; `row_version` gives optimistic
  concurrency; `RequireChangeReasonAttribute` enforces `X-Change-Reason` on edits/deletes. Every Phase 6
  entity inherits all of it by implementing `IAuditable` / `ISoftDeletable`.
- **The shared web editor is proven.** `components/documents/line-draft.tsx` (`LineDraftEditor`,
  `DraftLine`, `toMinor`, `useDraftTotals`, `CustomerCombobox`) backs the invoice, quotation and
  credit-note screens. PO and job-card screens reuse it; a `SupplierCombobox` mirrors `CustomerCombobox`.

**The one genuinely missing piece:** a **payables ledger**. The Phase 5 ledger is customer-only
(`LedgerEntry.CustomerId`, table `receivables_ledger`, `IReceivablesLedger.BalanceForCustomerAsync`).
Supplier invoices need a parallel, append-only, derived-balance ledger keyed by `SupplierId`. That is
slice 2's core, and it is the only place Phase 6 writes new ledger infrastructure rather than reusing it.

---

## What the legacy source says before we plan anything

Read against the live controllers at `C:\Users\Saboor.a\Desktop\SMARTNET_DEV\Smart_InvSys\Controllers`,
not assumed. The supply side is *thinner and more broken* than the sell side — which is why the plan
adds capability in two places rather than merely porting.

1. **Purchase orders are a printable document with no consequences.** `POController` /
   `SearchPOController` write `po_h` (`po_no, podate, supplier, totamount, preparedby, cdatetime,
   company, nonvattotal, vatty, vatpercent`) and `po_l` (`pono, itemno, desc, qty, rate, total`).
   Lines are **free-text** — `po_l.itemno` stores the cart's sequence number (1,2,3…), **not** a real
   `item_m` code, so a PO is not linked to inventory and **receiving moves no stock**. There is **no
   status/lifecycle column** at all — a PO is created, edited and reprinted, nothing more. Numbering is a
   per-company sequence-table insert (`po_seq_st` → `STPO-{id}`, `po_seq` → `SNPO-{id}`) — race-safe by
   accident (per-connection `LAST_INSERT_ID`), but the header/line inserts are **non-transactional**
   (`POController.cs:100,105`), so a mid-save failure orphans `po_l` rows, and the one document VAT is
   resolved by **`CURDATE()`** (`:72`), the same reprint-drift bug as invoices (B5/B6). A server-session
   cart (`Session["pocartitms"]`) builds the lines — the D4 pattern Phase 5 already killed.
2. **Supplier invoices are header-only money records with a binary flag.** `SupplierInvoicesController`
   writes **`supplier_invoice`** (`id, invno, supcode, amount, paymentstat, invdate, company, novattotal,
   vtype, vper`) — **no line table, no cart**. The user types the total `amount`; there is **no VAT or
   line math** (`novattotal/vtype/vper` are passed straight through). `paymentstat` is a binary
   `'Pending'` / `'Paid'` string; a payment inserts a `supplier_inv_pay` row (`supinvid, paiddate,
   referenceno, pay_method`) and flips the flag — **no partial payments** (`amount` is never reduced).
   The supplier's own `invno` is free text; the only system key is the auto-increment `id`. Supplier
   outstanding is **derived** (`SUM(amount) WHERE paymentstat='Pending'`), never stored — but the report
   scopes it to a date range, so it is not a true running balance (`SupplierPSumController.cs:145`).
   `deleteSupInv` hard-deletes the header and **orphans its payments** (`:85`). No stock impact anywhere.
3. **Job cards are a denormalized single table with a text-blob for lines.** `JobCardController` writes
   **`jobs_m`** — one row per card: `jdate, jobno, company, customer, contactperson, faultD` (fault
   description), `remarks, enteredby, entereddt, jobdoneby` (technician), `jstat` (`PENDING`/`CLOSED`),
   **`items`** (a newline-delimited **text blob**: `Item : … | Qty : … | Serial No : …`), and — set only
   on close — `cost, sell, completionremarks, completedby, dompleteddt` (note the misspelled column).
   "Serial tracking" is **one free-text serial string per line regardless of qty** — not per-unit, not
   unique, not searchable, and corruptible by a `|` in a name. `closeJob` flips `PENDING → CLOSED` and
   records cost/sell; it raises **no invoice** (the `invoice_h` insert is commented out,
   `JobCardController.cs:222`), moves **no stock**, and posts to **no ledger**. Numbering is
   `jobs_seq_st → STJ-{id}` / `jobs_seq → SNJ-{id}`. Close relies on `Session["selectedjno"]` and does
   **not** re-validate the job is still PENDING, so a closed job can be reopened/overwritten.
4. **Customer contacts are two independent `;`-separated strings.** `cus_m.contactp` (names) and
   `cus_m.email` (addresses) are semicolon-joined free text — *unpaired* lists (the Nth name does not
   correspond to the Nth email). Splitting is entirely **server-side C#**; the canonical splitter is
   `QuotationController.getQuoteContactP` (`:68-94`), which feeds the contact dropdown on every document
   screen. The **only** writers are `CustomerController` insert (`:88`) / update (`:95`). The per-document
   `contactperson` header column is a *snapshot* string (invoice_h / quotation_h / jobs_m) and stays a
   plain string — it is history, not a reference.

All four modules share the legacy habits Phase 5 already ended once (non-transactional multi-statement
saves, session carts, `double`/`varchar` money, `CURDATE()` tax resolution, no audit). Phase 6 ends them
for the supply side by **plugging into the Phase 5 engine** rather than re-solving them.

---

## Slice 0 — decisions taken before any code

Six settled from the research + your answers (2026-07-16). Nothing is left flagged: the confirmation
that *not every purchase has a PO* resolved the one open money/stock point cleanly (decision B′).

**A. New documents live in the *adopted* legacy tables, additively — the Phase 5 rule, unchanged.**
Adopt `po_h`/`po_l`, `supplier_invoice` (+ `supplier_inv_pay`) and `jobs_m` onto the new side exactly as
`invoice_h/l` were: a surrogate `id` PK, typed `decimal`/`date` columns *beside* the untouched legacy
`varchar` columns, `company_id`, a `prepared_by`/`entered_by` **user id**, `data_origin = 'new'`, a
nullable line→header FK, the seven audit columns — and the **legacy columns kept populated on write** so
the still-live legacy app keeps reading new rows. Genuinely new tables (the **payables ledger**, the new
**`jobcard_l`** line table, **`customer_contacts`**) use EF's generated `CreateTable`, since there is no
legacy table to preserve. The `varchar → DECIMAL`/`DATE` retype of the legacy columns still waits for
**Phase 9**.

**B. Purchase orders mirror the invoice — Item + Service on the shared engine, but they record the
*order*, not a receipt (your decisions 2026-07-16).** There is no "PO" and "service PO" as separate
controllers; there is one PO whose *lines* each either reference an `Item` (surrogate `ItemId`, carries a
cost — so the deferred GRN can later receive against the line) or are free-text service lines,
discriminated exactly like `InvoiceLine`. The PO runs through the **same `TaxEngine`**: one company rate
resolved at the **document's date** (fixing the legacy `CURDATE()` drift) and **snapshotted**, `decimal`
throughout, transactional numbering from `document_series` (the legacy `STPO-`/`SNPO-` sequences become a
templated series), versioned and audited. Unlike an invoice, a PO has **no cash/credit split and no
credit-limit gate** (it is a supplier order, not a customer sale), **posts no stock** (the GRN does that,
later — B′), and **posts no payable** (the supplier invoice does that — C). A Phase 6 PO is therefore a
*correct, tax-snapshotted, audited order document* — the spine that the GRN and the supplier invoice will
later hang off.

**B′. The purchasing flow is PO → GRN → supplier invoice; the GRN is deferred (your decision
2026-07-16).** A PO's full quantity rarely arrives at once, so goods are received against the PO in
**partial** quantities by a **GRN (Goods Received Note)** — the document that actually posts
`StockMovement.Receipt` for the received qty, and from which the PO's received-status (open → partially
received → fully received) is *derived*. That GRN is **out of Phase 6, deferred to a later phase.**
Consequences for Phase 6:

- The PO **posts no stock**. It records what was ordered; nothing moves inventory on PO save.
- **Stock-in stays on the existing Phase 3 Item Stock adjustment** for every purchase (PO-backed or
  ad-hoc) until the GRN lands. No new goods-receipt document is built here.
- Item PO lines still carry `ItemId` and cost **so the future GRN can receive against them** — the PO is
  GRN-ready without the GRN existing yet.
- **No Phase 6 document moves stock at all** (PO = order, supplier invoice = payable, job card = service
  tracking). The one stock-receipt path is entirely deferred with the GRN.

**C. Supplier invoices are a header-only AP record on a new payables ledger (your decision).** No line
items, no stock — **always** (goods arrive via a stock adjustment today, and via the GRN once it is built
— never the invoice; decision B′). The document captures the supplier's own `invno`
(free text, kept), the `amount`, the VAT breakdown fields, and `company_id`. The **payable** posts to a
new append-only **`payables_ledger`** keyed by `SupplierId` — entry types `OpeningBalance | Purchase |
Payment`, a supplier's balance is `Σ amount`, **never a stored column** (the receivables pattern,
mirrored). This buys two things the legacy app lacks: **partial payments** (each payment is a `Payment`
entry; the invoice settles when `Σ = 0`) and **"Paid" as a derived fact**, not a flag. The legacy
`paymentstat` and `supplier_inv_pay` rows are **dual-written** so legacy reports and the new Phase 4
supplier reports keep reading. `deleteSupInv`'s orphaning hard-delete is **not** ported — delete is soft,
reason-gated, and reverses the payable through a compensating entry.

**D. Job cards get structured, serial-tracked lines and a guarded workflow (your decision).** The
`jobs_m.items` text blob is replaced by a real **`jobcard_l`** line table — **one row per unit**, each
carrying its own **serial** (description, optional `item_id` reference, per-unit serial), so a qty-5 line
is five serialled rows, not one free-text string. The blob is **dual-written** (`Item : … | Qty : … |
Serial No : …`) so the legacy Crystal job sheet still prints. A job card has **no tax, no line totals, no
stock, and raises no invoice** (faithful to legacy scope — closing is a status event, not a sale). The
**workflow** is `PENDING → CLOSED`, guarded: close requires the job be PENDING (no silent reopen/overwrite
— the legacy `Session["selectedjno"]` hazard), is `row_version`-checked, and records `cost`, `sell`,
`completion_remarks`, `completed_by` (user id) and a properly-spelled `completed_at` (dual-writing the
legacy misspelled `dompleteddt`). Fault description and remarks are first-class header fields.

**E. Structured customer contacts, dual-writing the legacy strings.** A new **`customer_contacts`** table
(`id, customer_id, name, role, phone, email, is_primary`, audit columns) replaces the two `;`-separated
`cus_m` columns as the source of truth for *new* edits. On every customer save the app **dual-writes**
`cus_m.contactp` (`;`-join of contact names) and `cus_m.email` (`;`-join of contact emails) so the
still-live legacy app (its `getQuoteContactP` splitter, its customer grid) keeps working. A one-off
**backfill** splits existing production values into rows. Because `contactp` and `email` are **unpaired
independent lists**, the backfill cannot honestly pair the Nth name with the Nth email: it creates one
contact row per `contactp` entry (name, email null) and, when the customer has exactly one of each,
attaches the single email to it; multiple unmatched emails become **email-only contact rows** flagged in
Data Exceptions for a human to reconcile. The client-side `parseContacts` (`line-draft.tsx:90`) is
retired and the invoice/quotation contact pickers point at real `customer_contacts` rows.

**F. Opening balances & reconciliation, the Phase 5 way.** Legacy **pending** supplier invoices import as
`OPENING_BALANCE` payable entries (the stored `amount`, verbatim, per LEGACY-DATA-POLICY §2) so the
derived payable starts from history rather than recomputing it. A reconciliation pass recomputes a sample
of PO totals through the new `TaxEngine` and diffs the stored legacy figure (expect small `double` vs
`decimal` differences), recorded alongside the Phase 5 findings.

---

## Slice 1 — Purchase orders · ~0.75 week · *the Phase 6 adoption proof*

First, because it proves the Phase 6 adoption template for a new document type without needing the new
ledger or (deferred) stock receipt: a correct, tax-snapshotted, audited **order** document.

- **Adopt `po_h`/`po_l`.** A hand-written additive `Phase6AdoptPurchaseOrders` migration (the
  `Phase5AdoptInvoices` pattern): surrogate `id` PKs, a nullable `po_l.purchase_order_id` FK, typed
  `decimal` money + snapshotted tax columns, `company_id`, `supplier_id` (surrogate, resolved from
  `supcode`), `prepared_by` user id, `data_origin`, audit columns; legacy `varchar` columns untouched.
  A `PurchaseOrder`/`PurchaseOrderLine` aggregate (honestly typed, `IAuditable, ISoftDeletable`) with
  each line's `ItemId` nullable (item vs service), a `PurchaseOrderConfiguration` with a `*LegacyShadow`
  tuple list and a `data_origin='new'` query filter.
- **A `PurchaseOrderCreator` on the shared shape.** `CreateInCurrentTransactionAsync` /`CreateAsync`
  mirroring `QuotationCreator` (the no-ledger, no-stock variant): guard-in-transaction →
  `TaxEngine.Calculate` (company rate at the PO's date, snapshotted) → `AllocateAsync(companyId,
  DocumentTypes.PurchaseOrder, date)` → build header+lines + `SetLegacyShadow` → SaveChanges → version-1
  snapshot → commit. **No ledger and no stock posting** (decisions B, B′) — stock is the deferred GRN's
  job; item lines carry `ItemId`+cost so that GRN can later receive against them.
- **The series.** Seed/initialise a `document_series` row per company for `PO` (the legacy `po_seq_st`/
  `po_seq` max, via the `NumberSeriesInitialiser` pattern), templated so `STPO-`/`SNPO-` render at the
  document date.
- **`POST /api/purchase-orders`**, FluentValidation (supplier exists, each line is item-or-service,
  quantities positive, discount 0–100), and the web `new` page reusing `LineDraftEditor` + a new
  `SupplierCombobox` and item picker. List + read view + History tab.

**Exit:** a PO (item and service lines) records a correct `decimal` total with snapshotted VAT, item lines
linked to the item master carrying cost, an audit row and a v1 snapshot, all in one transaction — and it
moves **no** stock and posts **no** payable. Proven by unit + integration tests.

*(Built and tested. **Domain** — `PurchaseOrder`/`PurchaseOrderLine` (`IAuditable, ISoftDeletable`, item
vs service via a nullable `ItemId`), `IPurchaseOrderCreator`, request/result records. **Adoption** — the
hand-written additive `Phase6AdoptPurchaseOrders` migration on `po_h`/`po_l` (surrogate PKs, nullable
line→header FK, typed `decimal` columns beside the untouched legacy varchars, `data_origin`, audit
columns; `company_id` already present from the multi-company migration, so skipped), the
`PurchaseOrderConfiguration` with legacy-shadow lists (po_h's thinner header — `vatty`/`vatpercent`/
`nonvattotal`, no `it`/`contactperson`/`discountper`), `LegacySchema` expanded to the full `po_h`/`po_l`
shape, and the legacy `PoH` read-model extended with `id`/`data_origin`. **Creator** — the
`QuotationCreator` shape (no ledger, **no stock**): tax engine (rate at the PO's date, snapshotted — the
`CURDATE()` fix) → transactional number (`DocumentTypes.PurchaseOrder`, already declared;
`LegacyNumbering` already carries the `po_seq_st`/`po_seq` series) → header+lines+shadow → v1 snapshot →
commit. **API** — `PurchaseOrdersController` (list & read, new + legacy; tax-rate preview; create),
contracts + validator, DI, the existing `purchaseorder`/`search_po` permissions. **Web** — the api-client
regenerated with the PO types; a standalone `SupplierCombobox`; the PO list, new (reusing `LineDraftEditor`)
and read-with-History pages; the nav item promoted from its Phase-6 placeholder. Two integration tests
green (correct decimal totals + snapshot + audit + legacy shadow, no stock; a non-VAT service-only PO);
full API suite **401 passed**; web `tsc --noEmit` and `eslint` clean.)*

---

## Slice 2 — The payables ledger & supplier invoices · ~0.75 week · *the one net-new engine piece*

- **The payables ledger.** A new append-only, audited `payables_ledger` (`SupplierId`, `Type ∈
  {OpeningBalance, Purchase, Payment}`, signed `decimal Amount`, nullable `SupplierInvoiceId`,
  `OccurredAt`, `Note`), an `IPayablesLedger.BalanceForSupplierAsync` (`Σ amount`), and an EF
  `CreateTable` migration — the exact mirror of `receivables_ledger`/`ReceivablesLedger`. A supplier's
  outstanding is derived, never stored; a one-off importer seeds `OpeningBalance` from each legacy
  **pending** `supplier_invoice.amount` (verbatim — decision F).
- **Adopt `supplier_invoice` (header-only) + `supplier_inv_pay`.** Additive migration; a `SupplierInvoice`
  aggregate (no line collection), `data_origin`, audit columns, the legacy shadow set (`paymentstat`,
  `amount`, `novattotal`, `vtype`, `vper`, `invdate`, `supcode`, `company`).
- **A `SupplierInvoiceCreator`.** Guard-in-transaction → build header (+ legacy shadow) → SaveChanges →
  post a `Purchase` payable entry for the amount → v1 snapshot → commit. No number *allocation* (the
  supplier's own `invno` is the reference; the surrogate `id` is the key), though a `SUPINV` series is
  available if the business later wants a system number.
- **Partial payments & derived status.** `POST /api/supplier-invoices/{id}/payments` posts a `Payment`
  entry (dual-writing a `supplier_inv_pay` row and the `paymentstat` flag for legacy readers). "Paid" is
  `Σ ledger == 0` — computed, never stored. Pending/Paid list views filter on the derived balance.
- **Soft, reason-gated delete** reversing the payable through a compensating entry — not the legacy
  orphaning hard-delete.

**Exit:** a supplier invoice records a `Purchase` payable; two partial payments drive the supplier's
**derived** balance to zero and the invoice shows Paid; a legacy pending invoice appears as an opening
balance; delete reverses the payable and is recoverable. Proven by tests.

*(Built and tested. **Payables ledger** — `PayablesLedgerEntry` (`SupplierId`, `Type ∈ {OpeningBalance,
Purchase, Payment}`, signed `decimal`), `IPayablesLedger.BalanceForSupplierAsync`/`OutstandingForInvoiceAsync`
(Σ entries), `payables_ledger` via `CreateTable` — the exact mirror of the receivables ledger; a
`Phase6SeedSupplierOpeningBalances` migration imports the legacy pending amounts verbatim. **Adoption** —
`supplier_invoice` already had an `int id` under a non-unique KEY (Finding 6), so the hand-written
`Phase6SupplierInvoicesAndPayables` migration **promotes it to a `bigint` primary key** rather than adding
one, and adds the typed columns beside the legacy varchars; `LegacySchema` gained the full `supplier_invoice`
+ `supplier_inv_pay` shapes, and the legacy `SupplierInvoice` read-model `id`/`data_origin`. **Service**
(`SupplierInvoiceService`, both `ISupplierInvoiceCreator` + `ISupplierInvoicePayments`): create posts a
`Purchase` + v1 snapshot; a payment posts a `Payment` (dual-writing a `supplier_inv_pay` row + flipping
`paymentstat` to Paid at `Σ=0`) and refuses an over-payment; a void reverses the payable to zero through a
compensating entry and soft-deletes — not the legacy orphaning hard-delete. **API** — `SupplierInvoicesController`
(list & read, new + legacy with derived outstanding/status; create; record-payment; reason-gated void),
contracts + validators. **Web** — a `SupplierInvoices` lib + the list (Amount/Outstanding right-aligned,
Pending/Paid badge), a header-only new form (net/VAT/amount with a suggested gross), and a read view with a
**record-payment** dialog and a reason-gated **void**; the nav item added. Four integration tests green
(payable, two partial payments → derived zero + Paid + dual-written rows, over-payment refused, void
reversal); full API suite **407**; web `tsc`/`eslint` clean.)*

---

## Slice 3 — Job cards · ~0.75 week · *structured serial lines + a guarded workflow*

- **Adopt `jobs_m` + a new `jobcard_l` line table.** Additive migration on `jobs_m` (surrogate `id`,
  `company_id`, `customer_id`, typed `entered_at`/`completed_at`, `entered_by`/`completed_by` user ids,
  `data_origin`, audit columns; legacy columns incl. `items` blob and misspelled `dompleteddt` kept for
  dual-write). A **new** `jobcard_l` (`id, job_card_id, item_id NULL, description, serial, sort`) — one
  row per unit — created with `CreateTable`.
- **A `JobCardCreator`** (no tax, no ledger, no stock): allocate `DocumentTypes.JobCard`, write header +
  serial lines, **dual-write the `items` blob** so the legacy Crystal sheet still prints, v1 snapshot,
  commit. Fault description and remarks are header fields; technician (`jobdoneby`) captured at create.
- **The close workflow, guarded.** `POST /api/job-cards/{id}/close` requires the card be PENDING
  (rejects a re-close — the legacy `Session["selectedjno"]` hazard), is `row_version`-checked and
  reason-gated, records `cost`, `sell`, `completion_remarks`, `completed_by`, `completed_at`, flips status
  to CLOSED, and writes a new version snapshot. No invoice raised, no stock moved (decision D).
- **Web:** a job-card `new` page (customer + contact picker → real `customer_contacts` from slice 4 where
  available, header fault/remarks, a serial-line grid), the list with a PENDING/CLOSED filter, a read view
  with a Close action, and the History tab.

**Exit:** a job card is created with per-unit serial-tracked lines, prints through the legacy sheet
(blob dual-write intact), and moves PENDING → CLOSED exactly once with cost/sell captured; a second close
is refused. Proven by tests.

*(Built and tested. **Domain** — `JobCard` (`IAuditable, ISoftDeletable`, `Status ∈ {PENDING, CLOSED}`) +
a new `JobCardLine` (one serial per unit), `IJobCardCreator` + `IJobCardWorkflow`. **Adoption** — the
hand-written additive `Phase6AdoptJobCards` migration adds a surrogate `id` PK + the typed columns to the
keyless, **fully NOT NULL** `jobs_m`, and creates `jobcard_l` via `CreateTable`; `LegacySchema` gained the
full `jobs_m` shape (every column NOT NULL, matching the live charset/collation) and the legacy `JobsM`
read-model `id`/`data_origin`. **Service** (`JobCardService`, both interfaces): create allocates the number
(`DocumentTypes.JobCard`), writes the header + serial lines, **dual-writes every NOT NULL legacy column and
the `items` blob** (one line per serial, in the Crystal sheet's format), v1 snapshot; close is guarded —
PENDING-only (a re-close throws), `row_version`-checked, records cost/sell/completion + who/when, flips to
CLOSED, v2 snapshot. No tax, ledger or stock. **API** — `JobCardsController` (list & read, new + legacy
with the blob parsed back to lines; create; reason-gated close), contracts + validators. **Web** — a
`JobCards` lib, the list (PENDING/CLOSED badge), a new page (customer + contact picker, fault/remarks, a
serial-line grid), and a read view with the Close dialog (cost/sell/completion + reason) and a profit line
once closed; a Service nav section. Two integration tests green (booked PENDING with serial lines + blob +
v1; close records cost/sell + v2, a second close refused); full API suite **419**; web `tsc`/`eslint`
clean. The contact picker still splits the `;`-separated string — slice 4 repoints it at real rows.)*

---

## Slice 4 — Structured customer contacts · ~0.5 week · *retire the last `;`-separated string*

- **`customer_contacts`** (`id, customer_id, name, role, phone, email, is_primary`, audit) via
  `CreateTable`, with a `Customer.Contacts` navigation. The customer master UI gains add/edit/remove of
  multiple contacts.
- **Dual-write the legacy columns.** On every customer save the app recomputes `cus_m.contactp` (`;`-join
  of names) and `cus_m.email` (`;`-join of emails) inside the same transaction, so the live legacy app and
  its `getQuoteContactP` splitter keep working (the strangler rule).
- **Backfill** (decision E): a one-off tool splits existing `contactp`/`email`, creating a contact row per
  name; single-contact-single-email customers pair them; surplus unpaired emails become email-only rows
  surfaced in **Data Exceptions**. Idempotent, dry-run first, verified against a dev copy.
- **Repoint the pickers.** Retire `parseContacts` in `line-draft.tsx`; the invoice, quotation and new
  job-card contact pickers read `customer_contacts` rows instead of splitting a string client-side.

**Exit:** a customer has structured contacts; editing them keeps `cus_m.contactp`/`email` correctly
`;`-joined for legacy; existing data is backfilled with unpaired emails flagged; the document contact
pickers use real rows. Proven by tests + a legacy-read check.

*(Built and tested. **`customer_contacts`** (`id, customer_id, name, role, phone, email, is_primary`,
audit) via `CreateTable`, a `Customer.Contacts` navigation, soft-deletable with a query filter (the audit
interceptor soft-deletes everything, so a reconciled-away contact stays in the table but out of every read).
**Dual-write** — the customer save reconciles the contact rows (replace-all) and recomputes `cus_m.contactp`
(`;`-join of names) and `cus_m.email` (`;`-join of emails) in the same transaction, so the live legacy app
keeps reading. **Backfill** — `POST /api/customers/backfill-contacts`, permission-gated and idempotent
(skips customers that already have contacts): it splits the two `;`-lists, pairs a name with an email only
when the counts line up (they are independent lists), and turns surplus unpaired emails into email-only
rows flagged for Data Exceptions — nothing guessed, nothing lost. **API** — `Contacts` on `CustomerSummary`
+ `SaveCustomerRequest`, projected in `Summarise`. **Web** — the customer dialog's single Contact/Email
fields become a structured contacts editor (name/role/email/phone + primary + add/remove); the invoice,
quotation and job-card pickers now read the structured rows via `customerContactNames`, falling back to the
legacy string for customers not yet backfilled. One integration test (persist + reconcile-replace deletes
the removed rows); full API suite **430**; web `tsc`/`eslint` clean.)*

---

## Slice 5 — Numbering init, reconciliation & the E2E · ~0.25 week

- **Numbering initialisation** for `PO` / `SUPINV` / `JOBCARD` extended into `LegacyNumbering` /
  `NumberSeriesInitialiser` so "Settings → Numbering → Initialise from legacy" seeds the new series from
  the legacy `*_seq` max without moving behind an already-issued number.
- **Reconciliation** (decision F): recompute a sample of legacy PO totals through the new `TaxEngine`,
  diff the stored figure, append findings to
  [legacy-analysis/RECONCILIATION.md](legacy-analysis/RECONCILIATION.md).
- **The Phase 6 E2E** on the existing ephemeral-MariaDB harness (`tools/E2EHost`): login → raise a
  **PO** (item + service lines) and see its correct total → enter a **supplier invoice** and see the
  supplier payable → take a **partial payment** and see the derived balance fall → create and **close a
  job card** with a serial line. `npm run e2e`, green.

**Exit (the phase exit):** the PO / supplier-invoice-partial-payment / job-card-close case passes end to
end, and the PO reconciliation sample is recorded.

*(Built and verified. **Numbering** — no new code needed: `LegacyNumbering.All` already carried the `PO`
and `JOBCARD` series (added in the slice 1/3 prep), and supplier invoices are unnumbered (the supplier's
own reference); the E2E proves it, allocating `E2EPO-1` and `E2EJOB-1` transactionally. **Reconciliation**
— `tools/DbReconcile` gained a purchase-order pass (recompute `po_h.totamount` from `po_l` through the same
`TaxEngine`); run against the dev copy it reconciled **124 POs, 98.4% exact, 99.2% within a penny, max diff
0.02** — pure `double`/`decimal` residue, no material defects — appended to
[legacy-analysis/RECONCILIATION.md](legacy-analysis/RECONCILIATION.md) (which also refreshed the invoice
sample: 498 invoices, 99.6% within a penny). **E2E** — `tools/E2EHost` seeds a supplier + the PO/job-card
series; `e2e/phase6.spec.ts` drives, in a real browser, raising a PO, recording a supplier invoice and a
partial payment (the derived payable falls 100 → 70), and booking + closing a job card (PENDING → CLOSED).
`npm run e2e` green — **4 passed** (the Phase 5 invoice flow + the three Phase 6 flows).)*

**Phase 6 is complete.** Purchase orders, supplier invoices (on the new payables ledger), job cards
(structured serial lines + guarded close), and structured customer contacts all ship on the shared engine;
the service-cost gap the legacy service flow had is restored; and the whole is proven by the full API suite
(**430**) and a real-browser E2E. The deferred **GRN** (goods receipt against a PO, in partial quantities)
remains the one flagged follow-up (decision B′) — stock-in stays on the Item Stock adjustment until it lands.

---

## What Phase 6 does NOT do

- **No GRN / goods receipt — deferred to a later phase (decision B′).** A PO records the *order* and moves
  **no stock**; goods are received (in partial quantities) by a GRN that is out of Phase 6. Until it is
  built, stock-in stays on the **existing Phase 3 Item Stock adjustment**, and the PO's received-status
  lifecycle is not modelled.
- **No stock movement by any Phase 6 document.** PO = order, supplier invoice = payable, job card =
  service tracking. The one stock-receipt path is deferred with the GRN.
- **No payable on the PO, no stock on the supplier invoice.** Money and stock never cross.
- **No job-card invoicing or stock.** Closing a job is a status event; it raises no invoice and moves no
  stock (decision D).
- **No styled PDF templates.** POs, supplier invoices and the job sheet print from the plain document
  view / the legacy Crystal sheet (job card); the six **QuestPDF** templates are **Phase 8**.
- **No customer payments / cheque register / expenses** — those are **Phase 7**.
- **No legacy data remediation and no `varchar → DECIMAL` retype** — opening balances import verbatim and
  surface in Data Exceptions; the retype is **Phase 9**.

---

## Carried forward

| Item | Why it matters here |
|---|---|
| **Payables ledger is the only net-new infra** | Everything else (numbering, adoption pattern, audit, versioning, the web editor) is reused from Phase 5. Build it as the exact mirror of `receivables_ledger`. |
| **GRN is deferred (B′)** | The document that receives a PO's goods into stock (in partial quantities) is out of Phase 6. A PO posts no stock; item lines carry `ItemId`+cost so the future GRN can receive against them. When the GRN is built it posts `StockMovement.Receipt` and drives the PO's received-status. |
| **Stock-in stays on the Item Stock adjustment** | Until the GRN lands, every purchase (PO-backed or ad-hoc) receipts stock through the existing Phase 3 adjustment. No Phase 6 document moves stock. |
| **Money and stock never cross** | PO = order (no stock, no payable); supplier invoice = payable (no stock); linked only by an optional `po_no` reference. Nothing double-counts. |
| **Dual-write legacy columns** | New POs write the full `po_h/po_l` legacy row; supplier invoices dual-write `paymentstat` + `supplier_inv_pay`; job cards dual-write the `items` blob and misspelled `dompleteddt`; customers dual-write `cus_m.contactp`/`email`. Every legacy read must still work until Phase 9. |
| **`contactp` and `email` are unpaired lists** | The contacts backfill cannot honestly pair name[N] with email[N]; surplus emails become email-only rows flagged in Data Exceptions (slice 0-E). |
| **Job-card serials are per-unit now** | The legacy one-string-per-line blob becomes one serialled row per unit — a new capability the plan-of-record asked for; the blob is dual-written for the legacy Crystal sheet. |
| **`preparedby`/`enteredby` name-strings** | New documents carry a user **id** (`prepared_by` / `entered_by` / `completed_by`); the supplier reports stop joining on a display name. |
| **Supplier outstanding was date-scoped** | The legacy `SUM(amount) WHERE pending BETWEEN from AND to` is not a true balance; the new derived payable (`Σ ledger`) is the running figure. |
