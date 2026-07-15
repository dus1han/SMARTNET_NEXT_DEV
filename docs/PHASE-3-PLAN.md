# Phase 3 — Master data

~2.5 weeks. Customers, suppliers, items, stock. The phase that proves the Phase 2 abstractions by
cloning them four times — and the phase where a decision has to be made about a module the business
appears to have abandoned.

Parent: [DEVELOPMENT-PLAN.md](DEVELOPMENT-PLAN.md) · Previous: [PHASE-2-PLAN.md](PHASE-2-PLAN.md)

**Exit criterion (from the parent plan):** *the table/form/validation/pagination/export pattern is
proven and reusable.* If Customers needs infrastructure that Users did not, Phase 2 did not finish;
if Suppliers needs anything Customers did not, this phase has failed its own exit.

---

## What the data says before we plan anything

Counted against the live dev copy on 2026-07-14, not assumed:

| Table | Rows | State |
|---|---|---|
| `cus_m` | 223 | Live. In daily use. |
| `sup_m` | 86 | Live. |
| `item_m` | 500 | **Never referenced by a single document.** |
| `item_stock` | 6 | Six rows, all entered on one day in September 2025. No balance has moved since. |

### 🔴 The item link is thrown away at save

The business raises **two kinds of document**: *item* invoices and quotations, picked from the item
master, and *service* invoices and quotations, whose lines are free text. That is why the legacy app
has four controllers per document type (`Invoice` / `ServiceInvoice` / `EditItemInvoice` /
`EditServiceInvoice` — ISSUES D3).

The data says the item half of that does not survive contact with the database:

- **All 12,598 invoice lines have an empty `itemcode`.** So do 6,672 of the 6,674 quotation lines.
  The column exists. Nothing has ever written to it.
- Yet **780 of the 11,824 credit-invoice lines have a description that exactly matches an item's
  name.** So item invoices *are* being raised — the picker copies the item's **name** into the line's
  `desc` and then **discards which item it was**.
- Nothing on `invoice_h` records which kind of document it is either. `invtype` is `Cash` / `Credit`,
  not item / service.

So once an item invoice is saved, **it is indistinguishable from a service invoice**, and the
question *"how many of item I-153 did we sell this year, and at what margin?"* has no answer. Not a
slow answer — no answer. The join does not exist.

That is also why `item_stock` has 6 rows that have never moved: nothing has ever been linked to an
item, so nothing has ever consumed stock.

**The business keeps the two paths separate.** An item invoice is raised *from the item master*; a
service invoice is *typed*. They are different documents, entered by different people for different
jobs, and merging them into one screen with a mode people have to notice is how you get a service
line on an item invoice by accident. Confirmed with the business, 2026-07-14: **item invoices are not
being raised at all today** — which is why every line in the database is free text — but the path
must exist and must come from the master.

So: **two paths in the UI, one engine underneath.** The grid, the totals, the tax summary, the draft
and the save are the same code (that is what collapses D3's four controllers into one module); what
differs is where a line can come from. The item document's grid picks from the catalogue and cannot
free-type; the service document's grid types and has no catalogue. The user never sees a mode switch,
because they chose the document before they got here.

**An item that is not in the master gets added to the master** — the business's own answer, and the
only one that keeps a catalogue worth having. So the "new item" dialog lives *inside the invoice*: an
add-item that means *leave this invoice, go to another screen, come back and start again* is an
add-item nobody uses. They raise a service invoice instead, the catalogue goes stale, and it dies.
Which is exactly what has happened here.

**What this phase does about it:**

- `item_m` gets the columns it never had: **selling price, cost, tax rate, reorder level, unit**.
  Today it has exactly two — `itemcode` and `itemname` — so every rate on every invoice for three
  years has been typed by hand, and no default was ever available to type over.
- Phase 5's document line carries **`item_id` (nullable) + a line type**. Nullable because a service
  line genuinely has no item; recorded because an item line genuinely has one. One line table, one
  document module (which is what collapses D3's four controllers into one), and the join finally
  exists.
- The line-item editor prototype now runs in **either** mode — an item document that picks from the
  catalogue, or a service document that types — off the same grid and the same totals engine. It was
  built catalogue-only, which could not raise a service invoice at all: the document the business
  actually raises every day.

**Historical lines are not back-filled.** Matching 780 descriptions to item names by string equality
would be a guess, and a guess in a margin report is worse than a gap — see LEGACY-DATA-POLICY.md.
History stays as typed; the join begins the day Phase 5 ships.

### The other things the schema says

- **`cus_m`, `sup_m` and `item_m` have no primary key.** No `id`, no unique index — nothing prevents
  two customers with the same code (there are currently none; that is luck, per FINDING 6).
- **Every column is `varchar(100)`**, including `climit` (a credit limit) and `item_stock.unitcost`,
  `quantity`, `balance`, `indate`. All values currently parse (FINDING 5), so the retype is safe.
- **`pro` is a foreign key in spirit only** — it points at `profit_percent.id` (5%, 10%, 15%…), with
  nothing enforcing it.
- **`c_form` is the company the customer is assigned to** — `companies_m.id` (1 = Smart Technologies,
  2 = Smart Net). 65 customers on 1, 116 on 2, and **42 assigned to nothing at all**.

  ⚠️ **It is an indication, not a boundary.** Checked against the documents: customers assigned to
  company 2 have **533 invoices raised by company 1**, and customers assigned to company 1 have **128
  raised by company 2**. Both entities invoice each other's customers, routinely, and have done for
  three years.

  This is the same rule the business states about users (see the amendment in
  [PHASE-1-PLAN.md](PHASE-1-PLAN.md#slice-3)): **Smart Net and Smart Technologies are two trading
  entities, not two tenants.** The same staff work across both; there are no per-company users; every
  user sees every company. So `c_form` becomes `assigned_company_id` — a **default** on new documents,
  shown on the customer screen, enforcing nothing — and **no customer list is scoped by company**. A
  filter there would hide 116 customers from the people who invoice them every week.
- **198 of 223 customers have a credit limit of zero or blank.** So `creditlimitcheck` — which
  Phase 5 must enforce — today applies to 25 customers. Worth knowing before building a wall.
- **`item_stock.enteredby` is a name string** ("Chanaka Kotugoda"), the same `addedby` pattern the
  audit spine replaced. It becomes a user id.

---

## 🔴 The legacy positional INSERT — checked, and it was real

The blocker carried since Phase 1 — *"if the legacy app anywhere does a positional `INSERT INTO … VALUES (…)`
with no column list, those writes now fail"* — was checked against the legacy source at
`C:\Users\Saboor.a\Desktop\SMARTNET_DEV`. **It does, in 23 places, and Phase 1 has already broken three
of them.**

An `INSERT INTO t VALUES (…)` with no column list must supply a value for *every* column in the table.
Add one — an `id`, an audit column, a `company_id` — and MariaDB rejects the statement outright:

```
ERROR 1136 (21S01): Column count doesn't match value count at row 1
```

Proven against the dev database, not reasoned about: the old `INSERT INTO invoice_h VALUES (…18 values…)`
now fails, because Phase 1's migration added `company_id` to that table. **Saving an invoice, a quotation
or a purchase order from the legacy app is already broken on dev**, and would have broken in production
the moment those migrations were applied.

| Table | Positional INSERTs | Broken by |
|---|---|---|
| `invoice_h` | 13 | Phase 1 (`company_id`) — **already** |
| `quotation_h` | 2 | Phase 1 (`company_id`) — **already** |
| `po_h` | 1 | Phase 1 (`company_id`) — **already** |
| `cus_m`, `sup_m`, `item_m` | 3 | Phase 3 slice 1 (`id` + audit columns) |
| `invoice_l`, `quotation_l`, `po_l`, `cn_l` | 4 | Not yet — Phase 5 will |

**Fixed**: every one of the 23 now names its columns, which makes it immune to any column added after
it. The legacy app compiles. Its source is not under version control (ISSUES E1), so the original
controllers are backed up beside them.

**Found on the way**: four of those statements pass *fewer* values than the table has columns and
therefore **fail today, before any migration** — the quote→invoice conversion (`SearchQuotation`, two
of them), the invoice-edit line rewrite (`SearchInvoice`), and one quotation line path. They have been
throwing `Column count doesn't match value count` into a `catch` that returns `"terror"` to the browser.
Naming the columns they actually supply fixes them. **Worth asking staff whether quote→invoice
conversion has ever worked.**

### ⚠️ The deployment order is now load-bearing

1. **Rebuild and deploy the patched legacy app first.** On dev this is *overdue*, not optional: the old
   binary cannot save an invoice against the migrated dev schema.
2. **Then** run the migrations.

Doing it the other way round takes the business down. Nothing here is safe to apply to production until
step 1 has been rehearsed on staging (DEVELOPMENT.md §10).

---

## Slice 1 — Adopting the master tables · ~3 days

The same additive move Phase 1 made on `user_m`, applied to `cus_m`, `sup_m` and `item_m`. **The
legacy app is still live and still writes these tables**, so every step is additive and every legacy
`INSERT` must still succeed afterwards (DEVELOPMENT.md §8).

- **Surrogate key.** `id BIGINT AUTO_INCREMENT PRIMARY KEY`, added to each. A legacy `INSERT` that
  names no `id` still works — the column defaults. Rows become addressable by something that is not a
  hand-typed string.
- **Unique index on the code**, once checked (currently zero duplicates in all three).
- **Audit columns + `row_version`**, so every master record inherits the audit spine and the History
  tab works on a customer the day the screen exists.
- **`company_id`**, nullable + backfilled + indexed, as Phase 1 did for documents.
- **Retype**, verified safe: `climit` → `DECIMAL(18,4)`, `item_stock.unitcost`/`quantity`/`balance` →
  `DECIMAL(18,4)`, `indate` → `DATE`, `enteredat` → `DATETIME`. MariaDB will still accept the legacy
  app's numeric strings; what it will now reject is `"abc"`, which is the point.
- Domain entities + configurations: `Customer`, `Supplier`, `Item`, `StockBatch`.

**Exit:** a test proves a legacy-style `INSERT INTO cus_m (cuscode, cusname, …)` — no `id`, no audit
columns — still succeeds against the migrated schema, and that the row it creates is readable through
the new `Customer` entity.

---

## Slice 2 — Customers · ~3 days

**The clone test.** This screen is Users with a different schema, and if it needs one new piece of
infrastructure, Phase 2's exit criterion was a lie. Nothing new is permitted here: `DataTable`,
`useAppForm` + zod, `applyServerErrors`, `useReason`, `History`, the server-side Excel export.

- CRUD + soft delete (with a mandatory reason, as everywhere).
- Credit limit as `decimal`, and the profit-percent band as a real reference to `profit_percent`.
- VAT number, contact, address, type (Company / Individual).
- FluentValidation on the server, mirrored by the zod schema on the client. The server is the
  authority (B7 — today validation is client-side jQuery only and the API trusts whatever arrives).
- Excel export — every legacy list has one and staff rely on it.
- The History tab, which now has something to show.

**Exit:** the diff for this screen contains **no new components and no new hooks**. If it does, that
is the finding, and it goes back into `components/`.

---

## Slice 3 — Suppliers · ~2 days

The same screen again, and deliberately so: this is the measurement. Customers proves the pattern
works; Suppliers proves it is *cheap*.

- CRUD, soft delete (the legacy app has **no delete at all** here — F-series), export, history.
- **Timebox: two days.** If it takes longer, the abstraction is wrong and we fix the abstraction
  rather than paying the difference again in Phase 6 and Phase 7.

**Exit:** Suppliers took materially less code than Customers.

---

## Slice 4 — Items & stock · ~4 days

The catalogue becomes real, because half the documents are supposed to come out of it.

- Items: code, name, **selling price**, **cost**, **tax rate**, **reorder level**, unit. Every one of
  those columns is new, because `item_m` has only two.
- **`POST /api/items` is callable from inside the invoice screen**, not only from the items list. The
  item master is only ever as complete as the cheapest way to add to it.
- Stock: summary per item, batch breakdown (`item_stock`), and **adjustments as movements, never as
  an in-place edit of a balance**. B3's rule, applied before there is any stock to get wrong: the
  balance is *derived* from an immutable movement ledger. Getting this right in a table with six rows
  costs nothing. Getting it wrong is how `invoice_h.balance` happened.
- Reorder level → a "below reorder" filter on the list. That is the entire point of the field.
- Excel export, history, soft delete on both.

**Exit:** an item's stock balance equals the sum of its movements, proven by a test — and there is no
code path anywhere that writes a balance directly.

---

## What Phase 3 does NOT do

No documents. No pricing rules beyond a default price on an item. No stock valuation (FIFO/average) —
that is a real decision with tax consequences and it belongs with the documents engine that will move
the stock.

And no "while we're in here" cleanup of the legacy tables. Additive only, until Phase 9.

---

## Carried forward

| Item | Why it still matters |
|---|---|
| **Pricing the 500 items** | The catalogue cannot default a price it does not have. Somebody has to price the items that are actually sold, once. Phase 5's line editor lets the price be overridden per line, so this does not have to be perfect — but it does have to exist. |
| **`c_form`** | Holds `1` or `2` on every customer. You said you know what it is — tell me, and it gets modelled as a named field with real validation instead of an unexplained integer. |
| **Legacy positional INSERTs** | Phase 1's open blocker, now applying to three more tables. If the legacy app anywhere does `INSERT INTO cus_m VALUES (…)` with no column list, adding `id` breaks it. **Check before this migration reaches production.** |
| **`STQ-0` duplicate · 3 orphaned payments · Data Protection keys · numbering init** | Unchanged from Phase 2. |
