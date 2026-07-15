# Phase 4 — Dashboard & reports

~2.5 weeks. One role-aware dashboard and ten reports. The phase with the lowest blast radius — it is
read-only — and the highest visibility: it is the first time the new app is *obviously* better than
the old one, which is what earns the buy-in for the risky phases after it.

Parent: [DEVELOPMENT-PLAN.md](DEVELOPMENT-PLAN.md) · Previous: [PHASE-3-PLAN.md](PHASE-3-PLAN.md)

**Exit criterion (from the parent plan):** the three dashboard controllers collapse to one role-aware
dashboard, and the ten reports each render with an Excel export — with bulk dunning email moved off
the request thread onto a queued background job. If a second report needs infrastructure the first did
not, the reporting spine (slice 1) was not finished.

---

## What the legacy source says before we plan anything

Read against the live controllers at `C:\Users\Saboor.a\Desktop\SMARTNET_DEV`, not assumed. Every one
of the thirteen reporting controllers shares the same five habits, and the reporting spine exists to
end all five at once:

1. **Money is `double`.** `totamount`, `cost`, `balance`, `amount`, `expense_amount`, `sell` are read
   as `double.Parse(rw.Field<string>(…))` or `Field<double>()` on a `SUM`. Every figure is text in the
   database, parsed to binary floating point, summed, and formatted back to text (Finding 5). Several
   reports (`JobCardsReport`, `Cheque`, `Expenses`) never parse at all — the string is written straight
   into the spreadsheet cell, so a blank amount is a blank cell and a bad one throws.
2. **"Outstanding" is a mutable `balance` column, not a derived value.** Every dashboard and the whole
   outstanding report read `SUM(balance)` / `WHERE balance > 0`. That column is **wrong by Rs 1.55M
   today** (Finding 1) — see the outstanding slice below, because it changes what the report is allowed
   to claim.
3. **Status is a per-document string flag.** `vtype='1'` (VAT invoice), `paymentstat='Pending'|'Paid'`,
   `jstat` — no partial states, no history. A supplier invoice is Paid or Pending, never half-paid.
4. **Identities and dates are strings.** `preparedby`, `addedby`, `enteredby`, `createdby`,
   `completedby`, `jobdoneby` are free-text names, not user ids — so "sales by user" joins on a name
   that a rename breaks. `indate`/`invdate`/`expense_date` are `varchar`, parsed per row by
   `Split('-')`, which throws on the first malformed value (the aging calc in outstanding does exactly
   this).
5. **Every query is raw string-concatenated SQL** through an untyped `DataTable`, and every report
   round-trips its filters through `Session` between the *search* call and the *export* call — which is
   both a stale-filter bug and, in `CustomerVATRController`, a real one: `Session["cvatcomp"] = to`
   stores the end-date where the company id belongs, so the export's company filter is corrupt.

All ten legacy exports are ClosedXML workbooks written to a GUID folder on the web server's disk and
handed back as a path. The new app already has a streamed `IExcelExporter` (used by Customers,
Suppliers, Items) — reports reuse it and never touch the disk.

---

## Slice 0 decision — the dashboard is one screen, and the customer dashboard is gone

The legacy app has **three** dashboard controllers — `AdminDashboard`, `UserDashboard`,
`CustomerDashboard`. The parent plan says collapse them to one role-aware dashboard. Two refinements,
both settled:

- **`CustomerDashboard` is dropped entirely.** Confirmed with the business, 2026-07-15: *this part is
  not used.* It corroborates the data audit — **no customer-portal logins exist** (Finding 7) — and the
  standing decision to drop the customer-facing module. Nothing in Phase 4 renders it, and the
  `Session["cuscode"]`-scoped queries behind it are not ported.
- **The other two become one screen with two shapes**, chosen by permission, not by controller:
  - **Company view** (holds `dashboard`): current-month cash/credit/total sales, outstanding, and a
    daily sales chart, across the selected company — the old `AdminDashboard`.
  - **My view** (does not hold `dashboard`): the same shape scoped to the signed-in user — the old
    `UserDashboard`, which filtered `invoice_h.preparedby = <name>`. That name-string join is the legacy
    hazard #4; the new documents carry a `prepared_by` **user id** from Phase 5, so until then the user
    view reads the legacy `preparedby` string and is labelled as approximate.

Note the latent legacy bug to *not* reproduce: `UserDashboard`'s payments card is company-wide, not
user-scoped. The new user view either scopes it correctly or omits it — it does not silently show
everyone's payments under "mine".

---

## Slice 1 — The reporting spine · ~3 days

Nothing else is built first. This is Phase 4's equivalent of Phase 2's `DataTable`: get it right once
and ten reports inherit it; get it wrong and ten reports each re-implement a money parser.

- **`IReportQuery` / a typed report result.** Parameterised SQL (never concatenated), returning typed
  rows. Money parsed **once, defensively**, from the legacy `varchar` columns to `decimal` — a blank or
  non-numeric legacy value becomes `0` with the row flagged, never an unhandled throw (the legacy
  reports throw; three of them demonstrably do). Dates parsed the same way.
- **A report runs against the legacy tables read-only.** `invoice_h`, `payments`, `supplier_invoice`,
  `expense_tr`, `jobs_m`, `cheques` are still owned by the legacy app and are *not* adopted here — Phase
  5/6/7 adopt them. Reports read them through the read-only `SmartnetLegacyDbContext`, parameterised.
- **Server-side Excel export reusing `IExcelExporter`.** One code path, streamed, money as real numeric
  cells so a column sums. No GUID folders, no disk.
- **The filter contract.** A report takes its parameters on the *request*, not from `Session` — which
  kills the stale-filter round-trip and the `cvatcomp = to` class of bug outright. Standard filters:
  date range + company; a report adds its own (a supplier, an expense category) as needed.
- **Deny by default.** Every report endpoint carries its existing permission — `sales_rpt`,
  `customersales_rpt`, `cusvat_rpt`, `suppliervat_rpt`, `supplierpurchase_rpt`, `supplierpayments_rpt`,
  `expenses_rpt`, `jobcards_rpt`, `chequerpt`, `customer_outstanding` — all already in the catalogue and
  policy-enforced (the endpoint-authorization test covers them the day the endpoint exists).
- **The frontend report shell.** A filter bar (date range + company + report-specific fields), a
  `DataTable` of results, and the export button — so a new report is a query and a column set, exactly
  as a new list screen is in Phase 2.

**Exit:** two reports built on it differ only by their query and their columns; neither adds a
component, a money parser, or an export path.

---

## Slice 2 — The dashboard · ~3 days

One screen, the two views above, replacing three controllers.

- KPI cards: month sales (cash / credit / total), outstanding, and profit — profit computed
  `Σ(total − cost)` in `decimal`, from the parsed legacy columns, with the legacy caveat that `cost` is
  cost-at-current not cost-at-sale (the old `AdminDashboard` has the same limitation; Phase 5 fixes it
  by snapshotting cost on the line).
- One chart: daily cash-vs-credit sales for the period. **Charting library is chosen here** — the web
  app has none today. Constraint: it must satisfy the `dataviz` guidance and the "this is NOT the
  Next.js you know" note in `apps/web/AGENTS.md` (read `node_modules/next/dist/docs` before wiring it).
  Default recommendation: a light, dependency-thin chart (hand-rolled SVG or a small lib) over a heavy
  framework — the dashboard needs two chart types, not a BI suite.
- Role-awareness from the permission claims already in the JWT: `dashboard` → company view, otherwise
  the "my" view. No new authorization surface.

**Exit:** a signed-in admin sees the company numbers and a salesperson sees only their own, from one
route, in either theme.

---

## Slice 3 — Period reports · ~3 days

The clone test for the spine. Five reports whose shape is *filter by period + company, list, total,
export*:

- **Sales** (`sales_rpt`) — cash/credit/total with profit; export lists every invoice line.
- **Customer sales** (`customersales_rpt`) — sales grouped per customer, ranked by profit. (Its own
  legacy controller, `CustomerSalesController`, not part of the dashboard.)
- **Expenses** (`expenses_rpt`) — `expense_tr` by period/company, optional category. The legacy
  `addedby` name-string is shown as-is (legacy data), labelled approximate.
- **Cheques** (`chequerpt`) — `cheques` by period/company. Fix the legacy export bug where createdby /
  createddt / printeddt all overwrite one cell; "amount in words" is derived, not read from the null
  `inwords` column.
- **Job cards** (`jobcards_rpt`) — `jobs_m` with cost/sell/profit; profit only where `jstat` is not
  pending, as today. Defensive money parsing (the legacy `Sum(double.Parse(Cost))` throws on a blank).

**Exit:** the fifth report's diff is a query and a column set. If it is more, the spine goes back to
slice 1.

---

## Slice 4 — VAT & supplier reports · ~3 days

The reports with a filing consequence, so correctness matters more than the others.

- **Customer VAT** (`cusvat_rpt`) — output VAT on tax invoices (`vtype='1'`). VAT is derived
  `total − novattotal`, as today, but the **`cvatcomp = to` filter bug is fixed by construction** —
  parameters ride the request, not the session.
- **Supplier VAT** (`suppliervat_rpt`) — input VAT on `supplier_invoice` (`vtype='1'`), the mirror.
- **Supplier purchase summary** (`supplierpurchase_rpt`) — purchases per supplier with pending balance.
  The legacy per-supplier correlated subquery (`SUM(amount) WHERE paymentstat='Pending'`) becomes one
  grouped query. "Pending" is a whole-invoice flag (no partial payments) — reported as the legacy model
  is, not silently reinterpreted.
- **Supplier payments** (`supplierpayments_rpt`) — payments in a period for one supplier, across
  `supplier_invoice` + `supplier_inv_pay`.

**Exit:** the customer-VAT export filters by the company the user actually chose — the thing the legacy
report gets wrong.

---

## Slice 5 — Outstanding & queued dunning · ~3 days · *the one with real risk*

The heavy report, and the only place Phase 4 writes anything (an email log and a job queue).

### What "outstanding" is allowed to claim

The legacy outstanding report is `SUM(balance) … WHERE balance > 0` — and that `balance` column is
**overstated-in-reverse by Rs 1.55M across 45 invoices with negative balances** (Finding 1), plus one
invoice marked paid with no payment (Finding 2). Phase 4 is read-only and runs *before* the data-
remediation phase, so it **cannot fix this** — and it must not pretend to. Two rules:

- The report reads the legacy `balance` as-is, so it matches the statements the business already sends
  — but it **cross-references Data Exceptions** (built in Phase 1) and marks any customer whose invoices
  include a known duplicate/negative-balance defect. The number is shown *and* flagged, never shown as
  if it were clean.
- Aging is computed from `indate` with **defensive date parsing** (the legacy `Split('-')` throws on a
  bad date; the spine's parser does not).

### The queued dunning email — closes C6

Legacy `CusOutstandingController.emailOSBulk` is a synchronous `foreach` over every selected customer,
each iteration building a ClosedXML workbook to disk and opening a **blocking `SmtpClient.Send`** with a
`Timeout = 1_000_000` (~16.6 min), against hardcoded credentials. N customers serialise N Excel builds
and N SMTP handshakes on one HTTP request; one slow recipient hangs the lot.

The rewrite:

- **A background queue** (a hosted `BackgroundService` + an in-process `Channel`, matched to the
  existing DI — no new broker). The endpoint enqueues and returns immediately.
- **`email_log`** — one row per message: recipient, customer, status (queued/sent/failed), error,
  timestamps. The thing the legacy app has no equivalent of — today a failed dunning email is silent.
- **Async SMTP** through the Phase-1 `IMailSender`, using the **`mail_settings` configured in the UI**
  (encrypted at rest) — *not* hardcoded credentials — and a templated statement from
  `email_templates`.
- **A business gate, stated plainly:** bulk dunning sends customers a figure taken from the
  `balance` column that Finding 1 shows is wrong. The queue and the log are built here; **enabling a
  bulk send waits on the data-remediation decision** (delete the 49 duplicate rows and recompute, or
  the accountant's alternative). Emailing 223 customers a wrong balance is a business action, not a code
  one — the plan surfaces it rather than shipping a button that does it quietly.

**Exit (the phase exit):** selecting customers and pressing *Send statements* returns at once, the
messages appear in `email_log` moving queued → sent, and nothing blocks the request thread — with the
send itself gated on the remediation decision.

---

## What Phase 4 does NOT do

- **No writes to business data.** It reads the legacy tables; it does not adopt or migrate them (that is
  Phase 5–7). The only things it writes are `email_log` and the job queue.
- **No fixing the data.** The Rs 1.55M duplicates, the broken totals, the orphaned lines (Findings
  1–4) are a remediation phase before Phase 5. Phase 4 *reports and flags* them; it does not touch them.
- **No customer dashboard**, no customer portal — dropped, per the business and Finding 7.
- **No PDF statements** beyond the Excel export and the email template — styled PDF templates are Phase
  8.

---

## Carried forward

| Item | Why it matters here |
|---|---|
| **Rs 1.55M duplicate payments (Finding 1)** | The outstanding report and any dunning email report against the wrong `balance` until remediation. Phase 4 flags; the business decides. |
| **`preparedby` is a name-string** | The dashboard "my view" and the sales-by-user cut join on a name until Phase 5 gives documents a `prepared_by` user id. Approximate until then, and labelled so. |
| **Charting library** | None in the web app today. Chosen in slice 2 under the `dataviz` guidance and the custom-Next constraint; keep it thin. |
| **Data Protection keys · `STQ-0` · 3 orphaned payments** | Unchanged from Phase 3. Outstanding cross-references the orphaned-payment exceptions. |
