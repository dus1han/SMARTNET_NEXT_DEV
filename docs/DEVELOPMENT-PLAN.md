# Development Plan — SMARTNET_NEXT

Decisions taken (2026-07-14):

| # | Decision |
|---|---|
| 1 | **Web catalogue dropped** — `WCategory`, `WProducts` not migrated |
| 2 | **Customer portal dropped** — `CustomerDashboard` and customer-type (`cuscode`) logins not migrated |
| 3 | **Invoices stay editable** — no forced immutability. **Mitigation: every edit is versioned and audited** (see §Invoice edits) |
| 4 | **Multi-company by design** — more entities are expected; `company_id` is a first-class dimension, not a hack |
| 5 | **Business rules become settings** — credit limits, payment terms, stock reorder levels |
| 6 | **Everything is versioned and audited** — who, when, why — on every entity, enforced at the persistence layer. See [AUDIT.md](AUDIT.md) |
| 7 | **Legacy data is left as-is; errors are prevented from cutover forward.** No historical remediation. Balances import as an *opening balance*, not a recomputation. Known defects surface in a **Data Exceptions** screen for correction when the business chooses. See [LEGACY-DATA-POLICY.md](LEGACY-DATA-POLICY.md) and [DATA-AUDIT-FINDINGS.md](DATA-AUDIT-FINDINGS.md) |

Scope removed by decisions 1–2: 14 legacy actions (`WCategory` 3, `WProducts` 5,
`CustomerDashboard` 3, `Home` 3). Remaining: **~234 legacy actions → ~120 endpoints**
after the Item/Service/Edit controllers are collapsed.

---

## Multi-company model (decision 4)

`company_id` is on every **document** and every **settings** row:

- **Per company:** document numbering series, tax rates & VAT-registration status,
  document templates, mail settings, email templates, branding.
- **Shared across companies:** customers, suppliers, items, item stock, users.
  *(A user's access to a company is granted; a customer can be billed by either entity.)*
- **Every document** (quotation, invoice, credit note, PO, supplier invoice, job card,
  payment, cheque, expense) carries `company_id`.

UI: a company switcher in the app shell. The active company scopes every list and drives
numbering, tax behaviour and the document header.

⚠️ **This is the decision most expensive to reverse.** Adding `company_id` to documents on
day one is nearly free; retrofitting it after 20,000 invoices exist is not. It is in the
schema from Phase 1 even though only two companies exist today.

---

## Versioning & audit (decisions 3 + 6)

Full spec in **[AUDIT.md](AUDIT.md)**. In summary — it applies to *everything*, not just invoices:

- **Audit columns** on every table: `created_by/at`, `updated_by/at`, `deleted_by/at`,
  `row_version`. User **id**, not a display-name string. UTC. **Nothing is hard-deleted.**
- **`audit_log`** — append-only, written by an EF Core `SaveChanges` interceptor **inside the
  same transaction** as the business change. A developer cannot forget to audit it, because
  there is no audit code to write. The app's DB user has `INSERT`/`SELECT` only — **no
  `UPDATE`/`DELETE`** — so the log cannot be rewritten by the application.
- **`document_versions`** — a full JSON snapshot of every financial document at every version,
  including v1. Self-contained (carries the resolved tax rates and company header as they
  stood), so reprinting last year's invoice reproduces *that* document, not today's VAT rate
  applied to old lines.
- **Reason capture** — mandatory on invoice edits, deletes, permission changes and settings
  changes; optional elsewhere. Enforced server-side via an `X-Change-Reason` header, not by a
  hopeful frontend.
- **Login (success and failure), print, email and export are audited too** — so "who exported
  the customer list?" and "was this invoice ever emailed?" become answerable. Today neither is.
- **`row_version`** gives optimistic concurrency: two users editing the same invoice now
  conflict loudly instead of one silently overwriting the other.

This is what makes decision 3 (invoices stay editable) defensible: you keep the workflow your
staff have, and an auditor can still be answered.

**Closes:** F1, F2, B3, and the silent-overwrite concurrency hole.

---

## Settings surface (decision 5)

Beyond the settings already planned (companies, numbering, tax, templates, mail):

| Setting | Today |
|---|---|
| Customer credit limit + **enforcement on/off** | Hardcoded check, service invoices only (`creditlimitcheck`) |
| Default payment terms (days) | Not present |
| Stock reorder level / low-stock threshold | Not present |
| Quotation default validity period | Pushed per-document as `qvalidity` |
| Default discount policy / max discount % | Unrestricted |
| VAT rounding mode (per line vs per document) | Implicit in `double` math |
| Invoice due-reminder days | Not present |

---

## Phases

Effort assumes **one experienced full-stack developer**. Ranges, not promises.

### Phase 0 — Foundations · ~1 week
- `git init` (done), monorepo layout, `.editorconfig`, CI (build + test on push).
- **Rotate the MySQL password. Rotate the SMTP password.** Restrict DB network access.
- `mysqldump` schema + data → **dev database**. Never develop against production.
- `docker compose`: api, web, mysql (dev), nginx.
- Scaffold EF Core entities from the existing schema (Pomelo).
- Nginx routes everything to the legacy app for now — one flag flips a path to the new stack.

**Exit:** `docker compose up` gives a running API and web shell against a dev copy of the real schema.

---

### Phase 1 — Auth, users & settings · ~3 weeks · *the security fix*
The whole point of going first: this closes most of the critical defects.

**Backend**
- `password_hash` column added alongside plaintext; Argon2id; **hash upgraded on each
  successful login** so both apps keep working during cutover. Forced change for anyone
  still on `1234`.
- JWT in an httpOnly, SameSite cookie. The 36 `user_permissions` flags → claims →
  **ASP.NET Core policies enforced on every endpoint**. Deny by default.
- Users: list, create, edit, disable, reset password, assign permissions.
- Settings: companies (+ VAT-registered flag, branding, bank details), document series,
  tax rates, document templates, mail settings (**encrypted, write-only, send-test**),
  email templates, app settings, business rules (§Settings surface).
- Cross-cutting from day one: Serilog structured logging, global exception handler
  (**generic error to client + correlation id**), EF Core migrations, `company_id` on the
  schema, audit columns (`created_by`, `created_at`, `updated_by`, `updated_at` — user *id*, UTC).

**Frontend**
- Login, change password, user admin, the full settings area, company switcher.

**Closes:** A1 A2 A2b A2c A3 A4 A5 A6 A7 A9 A10 · B7 B8 · E2 E4 · F2
**Exit:** an admin changes company header, VAT rate, invoice prefix, credit-limit policy and
SMTP — from the UI, no deployment. A non-admin calling an admin endpoint gets 403.

---

### Phase 2 — Design system & app shell · ~1.5 weeks *(overlaps Phase 1)*
Tailwind + shadcn/ui, light/dark, permission-driven navigation, company switcher,
data-table and form abstractions (used by every later module), motion primitives,
skeletons, toasts. Generated TypeScript client from the API's OpenAPI schema.

**Exit:** one reference CRUD screen the rest of the app is cloned from.

---

### Phase 3 — Master data · ~2.5 weeks
Customers (+ credit limit), suppliers (**+ delete**), items (**+ delete**), item stock
(summary, batch breakdown, adjustments, **reorder level**). Excel export on each.

**Exit:** the table/form/validation/pagination/export pattern is proven and reusable.

---

### Phase 4 — Dashboard & reports · ~2.5 weeks
One role-aware dashboard (replacing three controllers) with charts + KPI cards.
Ten reports — sales, customer sales, outstanding, customer VAT, supplier VAT, supplier
purchase summary, supplier payments, expenses, job cards, cheques — each with Excel export.

Outstanding gets **bulk dunning email**, but now as a **queued background job** with an
`email_log`, not a 16-minute HTTP request. (Closes C6.)

Read-only, so low blast radius — and it's the phase that makes the new app visibly better
than the old one, which matters for buy-in.

---

### Phase 5 — Documents engine · ~5 weeks · *the bulk and the risk*
Quotations, invoices, credit notes. This is where the legacy app's 4-controllers-per-document
collapses into one module.

- **Line items move to the client.** The session "cart" (`addtoCart` / `cartLoad` /
  `removeQItem`) is deleted; the browser holds the draft and posts the document whole.
  *This is the single biggest behavioural change in the migration.* (Closes D4.)
- One tax engine, called by every document. `decimal` throughout. Per-line tax rate,
  snapshotted at save. Mixed-rate documents work — today they cannot. (Closes B1 B5 B6 D2.)
- Numbering allocated **transactionally** from `document_series` (`SELECT … FOR UPDATE`).
  (Closes B4.)
- Every save is **one transaction**. (Closes B2.)
- Balances derived from a ledger, never mutated in place. (Closes B3.)
- Invoice versioning + audit (§Invoice edits). Credit limit enforced per settings.
- Quote → invoice conversion. Deleted-invoice audit list.

**Exit:** an invoice with mixed VAT rates, a discount and a partial payment produces
correct totals, a correct ledger, and a correct audit record — proven by tests.

---

### Phase 6 — Purchasing & service · ~3 weeks
Purchase orders, supplier invoices (pending/paid), job cards (fault description, remarks,
serial-tracked lines, close-job workflow). Same engine, same patterns.

**Structured customer contacts.** Retire the `;`-separated `customer.contactperson`/`email` fields
for a real `customer_contacts` table (name, role, phone, email per row). Additive migration that
**dual-writes** the legacy `varchar` columns so the still-live legacy app keeps reading, plus a
**backfill** that splits existing `;`-separated production values into rows. The customer master
gains add/edit/remove of multiple contacts, and the Phase 5 invoice contact-picker — which today
splits that one string client-side — points at the real rows instead. *(Deferred here from Phase 5,
2026-07-15: it is master-data plumbing, not part of the documents engine.)*

---

### Phase 7 — Money & documents · ~2.5 weeks
Customer payments (settle against open invoices), cheque register, expenses + categories,
document storage (**object storage, type/size whitelist, server-generated names** — closes
A8/C3/C4), notes.

---

### Phase 8 — PDF templates · ~2.5 weeks · *deferred, as agreed*
Six QuestPDF templates (invoice, quotation, credit note, PO, job sheet, cheque) driven by
`companies` + `document_templates`. Field contracts already extracted —
`docs/legacy-analysis/REPORT-FIELDS.md` and `rpt/rpt-dump.json`.

⚠️ **The cheque is not a redesign.** It overlays pre-printed stationery and must stay
pixel-exact. Measure a real cheque.

Claude drafts each template; you adjust to the required format.

---

### Phase 9 — Cutover & decommission · ~1.5 weeks
Nginx sends all traffic to the new stack. Drop the plaintext `password` column. Delete
`DBConnect`. Retire the Windows/Crystal dependency. Archive the legacy app.

---

## Timeline

| Phase | Effort | Includes |
|---|---|---|
| 0 · Foundations | 1 wk | |
| 1 · Auth, users & settings | **4 wk** | +1 wk: audit infrastructure (columns, `audit_log`, interceptor, versions, reason capture) |
| 2 · Design system *(overlaps)* | **2 wk** | +0.5 wk: reusable History tab (version list, diff, restore) |
| 3 · Master data | 2.5 wk | inherits audit for free |
| 4 · Dashboard & reports | **3 wk** | +0.5 wk: admin Audit Log viewer |
| 5 · Documents engine | 5 wk | document versioning already in place |
| 6 · Purchasing & service | 3 wk | |
| 7 · Money & documents | 2.5 wk | |
| 8 · PDF templates | 2.5 wk | *deferred* |
| 9 · Cutover | 1.5 wk | |
| **Total** | **~26 weeks / ~6 months** | one developer, sequential |

Audit adds ~2 weeks, all of it early. Retrofitting it later would touch every module — which
is precisely why it goes in Phase 1, before a single business entity is written.

The strangler approach means value ships from Phase 1 — you are not waiting 5 months for
a big-bang release.

---

## Risks

| Risk | Mitigation |
|---|---|
| **The cart rewrite (Phase 5)** changes how staff enter documents | Prototype the line-item editor in Phase 2 and put it in front of a real user before Phase 5 |
| **Money correctness** — `double` → `decimal` will surface historical discrepancies | Reconcile a sample of existing invoices during Phase 5; expect small differences and decide policy |
| **Dual-write window** — legacy app keeps writing plaintext passwords | Keep hash-on-login until legacy is dead (Phase 9) |
| **Cheque printing** | Pixel-exact, physical stationery — do not modernize |
| **Scope creep via settings** | Settings that nobody changes are cost. Only build the seven in §Settings surface |
| **Schema is the legacy schema** | Deliberate. Refactor it *after* cutover, not during |
