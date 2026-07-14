# Smart_InvSys → Next.js + .NET Core: Migration Path

**Target:** Next.js 15 (App Router, TS) + ASP.NET Core 9 Web API + existing MySQL (`smartnet_invsys`)
**Hosting:** Linux + Docker, Nginx reverse proxy
**Strategy:** Strangler — old MVC app and new stack run against the same DB; modules cut over one at a time.
**Core principle:** Company identity, document headers, tax rates, numbering, and formatting are **configuration rows, not code**.

---

## 0. Prerequisites (do these before writing any code)

| # | Task | Why |
|---|------|-----|
| 0.1 | **Rotate the MySQL password** and restrict network access to the DB | Credentials are hardcoded in `DBConnect.cs:28-34` and have been distributed with every copy of this folder |
| 0.2 | `git init` the existing app, commit as-is | There is no source control today. This is the rollback point. |
| 0.3 | `mysqldump` the schema + a data snapshot → **dev database** | Never develop against production. |
| 0.4 | Document the current `user_permissions` 36 flags and the SN/ST company differences | These become the settings model. |

---

## 1. Repository layout

```
smartnet/
├─ apps/
│  ├─ api/
│  │  ├─ Smartnet.Api/             # ASP.NET Core 9, endpoints, auth, policies
│  │  ├─ Smartnet.Domain/          # entities, tax engine, numbering, doc rules
│  │  ├─ Smartnet.Infrastructure/  # EF Core (Pomelo), scaffolded from MySQL
│  │  └─ Smartnet.Documents/       # QuestPDF templates + ClosedXML exports
│  └─ web/                         # Next.js 15, Tailwind, shadcn/ui, Framer Motion
├─ packages/api-client/            # TS types generated from OpenAPI
├─ infra/
│  ├─ docker-compose.yml           # api, web, nginx, mysql (dev)
│  └─ nginx/                       # routes ported paths → new stack, rest → legacy
└─ MIGRATION.md
```

Scaffold the data layer from the existing schema — this hands you in one command the domain model the current app never had (`Models/` is empty):

```bash
dotnet ef dbcontext scaffold "Server=...;Database=smartnet_invsys;..." \
  Pomelo.EntityFrameworkCore.MySql \
  -o Entities --context SmartnetDbContext --no-onconfiguring
```

---

## 2. The configuration layer (build this first — everything depends on it)

This is what removes hardcoding. New tables, additive to the current schema so the legacy app keeps running.

### `companies` — replaces the SN/ST report duplication
```
id, code (SN|ST), legal_name, trade_name,
trn / vat_reg_no, license_no,
address_lines, phone, email, website,
logo_url, stamp_url, signature_url,
bank_name, iban, account_no, swift,
brand_color, is_default
```
Twelve `.rpt` files (6 documents × 2 brandings) collapse to **6 QuestPDF templates + 2 company rows**. Adding a third company later is a row, not a release.

### `document_series` — configurable numbering per company/doc type
```
company_id, doc_type (INV|QUO|CN|PO|JOB|CHQ),
prefix, suffix, next_number, padding,
reset_policy (never|yearly|monthly)
```
Numbering must be allocated **transactionally** in the API (`SELECT ... FOR UPDATE`), not by reading MAX(id).

### `tax_rates` — evolve, don't replace, `vat_ty` + `vat_validity`
Keep the date-ranged design (it's already correct). Expose it in a settings UI:
```
tax_code (VAT5|VAT0|EXEMPT|RCM), label,
rate_percent, valid_from, valid_to,
applies_to (goods|services|both), is_default
```
**Critical rule:** when a document is saved, **snapshot the resolved tax rate onto each line** (`tax_code`, `tax_rate_applied`). Otherwise a future VAT change silently rewrites historical invoices — an audit failure.

### `document_templates` — header/footer/terms per company per doc type
```
company_id, doc_type,
header_mode (letterhead|full|minimal),   # e.g. blank paper vs pre-printed
show_logo, show_stamp, show_signature, show_bank_details,
terms_text, footer_note, declaration_text,
visible_columns (json), column_labels (json)
```

### `app_settings` — key/value for global behaviour
```
currency, currency_symbol, decimal_places,
date_format, timezone, rounding_mode (line|document), rounding_precision,
default_payment_terms_days, fiscal_year_start,
invoice_due_reminder_days
```
*(SMTP lives in `mail_settings` below — it is per-company and holds a secret, so it does not
belong in a plain key/value table.)*

### Tax engine (`Smartnet.Domain/Tax/`)
One service, used by every document type:
```csharp
TaxResult Calculate(IEnumerable<Line> lines, DateOnly docDate, TaxOptions opts);
// resolves the rate effective on docDate, applies line- or document-level
// rounding per app_settings, returns per-line + per-rate summary + totals
```
Every document (quotation, invoice, credit note, PO, supplier invoice) calls this one path. Today the tax math is duplicated across ~15 controllers.

### `mail_settings` — SMTP per company, credentials encrypted at rest
Today the SMTP host, port and **password in plaintext** are hardcoded in
`CusOutstandingController.cs:157` and duplicated again at `:304`.

```
company_id,
host, port, use_ssl,
username, password_encrypted,     # encrypted at rest — never returned to the client
from_address, from_display_name,
reply_to, bcc_address,            # bcc accounts on every outbound document
send_enabled,                     # kill switch
daily_send_limit
```

Rules:
- The password is **write-only over the API**. The settings UI shows `••••••` and posts a new
  value or nothing; a GET never returns it.
- Encrypt at rest (ASP.NET Core Data Protection / KMS), do not merely base64 it.
- Provide a **"Send test email"** action in the settings UI — otherwise misconfiguration is
  only discovered when a customer doesn't get their invoice.
- Per company, because Smart Net and Smart Technologies are different legal entities and
  should not send from the same address.

### `email_templates` — the five mail flows, currently hardcoded
The app sends mail in five places (`emailIPDF`, `emailQPDF`, `emailPOPDF`, `emailOS`,
`emailOSBulk`), each with its subject and body written into C#.

```
company_id, template_key,          # INVOICE | QUOTATION | PO | OUTSTANDING | OUTSTANDING_BULK
subject,                           # supports {{invoice_no}}, {{customer_name}}, {{total}}, {{due_date}}
body_html, body_text,
attach_pdf, attach_excel,
is_active
```

Token substitution rather than string concatenation, so finance can reword a dunning letter
without a deployment.

### Sending is a background job, not a web request
`emailOSBulk` currently loops customers, builds an Excel file and sends SMTP **inside the HTTP
request**, with a 1,000,000 ms timeout (`CusOutstandingController.cs:161`). One slow mail server
holds a request thread for sixteen minutes.

**Standard:** queue the send (hosted service / Hangfire), persist an `email_log`
(`to`, `template_key`, `document_ref`, `status`, `error`, `sent_at`) so you can answer
"did the customer actually get their invoice?" — which today is unanswerable.

---

## 3. Phased delivery

Each phase ships behind the Nginx router: ported paths go to Next.js, everything else stays on the legacy app.

### Phase 1 — Platform core: Auth + Settings
- `password_hash` column added alongside plaintext `password`; new API writes bcrypt/Argon2 and **upgrades each user's hash on successful login**. Drop the plaintext column once fully populated. Both apps keep working during this.
- JWT in an httpOnly cookie. The 36 `user_permissions` flags become **claims → ASP.NET Core authorization policies enforced per endpoint**. (Today they only hide UI; every data endpoint is open to any logged-in user.)
- Settings admin UI: companies, document series, tax rates, templates, app settings.
- **Exit criteria:** an admin can change the company header, VAT rate, and invoice prefix from the UI with no code change.

### Phase 2 — Design system + app shell
Tailwind + shadcn/ui, dark mode, permission-driven navigation, motion primitives (see §4), skeleton loaders, toasts.

### Phase 3 — Master data
Customers, suppliers, items, item stock. Simple CRUD — locks in the table / form / validation / pagination patterns everything else copies.

### Phase 4 — Documents engine (the bulk)
Quotations, invoices, credit notes, purchase orders, job cards, supplier invoices.

The legacy app has **separate controllers for item vs. service vs. edit** variants of nearly every document (`ItemQuotation`, `ServiceInvoice`, `EditItemInvoice`, `EditServiceQuotation`, …). These collapse into **one form + one API resource per document type**, with line-type as a field. This is where the code count drops hard.

Shared pieces: line-item editor component, tax engine, numbering service, status workflow.

### Phase 5 — PDF + Excel
QuestPDF templates driven by `companies` + `document_templates`. ClosedXML exports carry over from the current code with minimal change (it runs on .NET Core).

### Phase 6 — Reports & dashboards
Sales, customer sales, VAT (customer + supplier), outstanding, expenses, supplier payments, job cards, cheques. Read-only, so low risk. Recharts on the frontend.

### Phase 7 — Tail
Document storage, notes, cheque register, customer payments, customer portal.

### Phase 8 — Cutover
Legacy app receives no traffic → decommission. Drop the plaintext password column. Remove `DBConnect`.

---

## 4. Frontend: modern, animated, but it's an ERP

Users create invoices all day. Motion must never delay input.

**Rules:** 150–250 ms, ease-out, honour `prefers-reduced-motion`, never animate while typing.

| Use | Don't use |
|-----|-----------|
| Staggered row fade-in as results load | Page transitions on every navigation |
| Count-up on dashboard KPI tiles | Parallax / scroll-jacking |
| `layout` animation when invoice lines are added/removed | Anything blocking form submit |
| Slide-over sheet for the line-item editor | Spinners (use skeletons) |
| Toast on save, inline field-level errors | Modal chains |

Most of the "modern" impression comes from typography, spacing, and a coherent color system — not motion. shadcn/ui plus a real design pass already looks generations ahead of Bootstrap 3.

**Stack:** TanStack Query (server state) · TanStack Table (grids — the heart of this app) · react-hook-form + zod (forms) · Framer Motion · Recharts · sonner.

---

## 5. Security fixes this migration bakes in

| Current | After |
|---|---|
| 346 string-concatenated SQL sites, 0 parameterized | EF Core — parameterized by construction |
| Plaintext passwords, default `1234` | Argon2/bcrypt, forced change on first login |
| Auth = "does a session exist" | Per-endpoint policy from `user_permissions` |
| No CSRF token, state changes via GET | SameSite cookies + proper verbs |
| DB credentials in source | Env vars / Docker secrets |
| `debug="true"`, `ex.ToString()` to client | Structured logging (Serilog), generic client errors |

---

## 6. Risks

- **Crystal → QuestPDF is the most underestimated task.** Budget real time for the 6 templates; get one invoice signed off before building the rest.
- **Tax rate snapshotting** — miss it and historical documents mutate.
- **Numbering races** — must be transactional.
- **Dual-write window** — while both apps are live, the legacy app writes plaintext passwords. Keep the hash-upgrade-on-login logic until legacy is dead.
- **Scope:** 44 controllers → ~20 modules. Months, not weeks, for one developer. The strangler approach means you ship continuously anyway.
