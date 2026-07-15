# Phase 2 — Design system & app shell

~2 weeks. The phase whose entire output is *leverage*: nothing here is a feature, and everything
here is used by every module from Phase 3 to Phase 8.

Parent: [DEVELOPMENT-PLAN.md](DEVELOPMENT-PLAN.md) · Previous: [PHASE-1-PLAN.md](PHASE-1-PLAN.md)

**Exit criterion (from the parent plan):** *one reference CRUD screen the rest of the app is cloned
from.* If a later module has to invent its own table, its own form, or its own error handling, this
phase did not finish.

---

## Why this is worth two weeks

The legacy app has 44 controllers and no shared anything: each screen re-implements its own grid,
its own validation, its own error display. That is why a bug fixed on one screen persists on the
other forty-three, and why "add a column to a list" is a day's work in six places.

The measure of success here is not how the reference screen looks. It is how little code the
*second* screen needs.

---

## Slices

### 1 · Generated API client · ~1 day

`packages/api-client`, generated from the API's OpenAPI schema. **Never hand-written**
(DEVELOPMENT.md §5).

Today `apps/web/src/lib/*.ts` hand-declares `UserSummary`, `MailSettings`, `CompanyProfile` and the
rest. They are correct *now*, because I wrote them from the C# an hour earlier. They will drift the
first time somebody adds a field to a DTO and does not think about the frontend — and TypeScript
will keep compiling, because it will simply not know the field exists.

- `openapi.json` emitted from the API at build time.
- `openapi-typescript` → typed paths, requests and responses.
- `npm run generate:api`, and a **CI check that fails if the committed client is stale**. A
  generated file that nobody regenerates is a hand-written file with extra steps.

**Exit:** deleting a field from a C# DTO breaks `npm run typecheck`.

---

### 2 · Design tokens & primitives · ~2 days

Tailwind 4 + shadcn/ui. Light and dark, driven by CSS variables in one place.

Button, Input, Select, Checkbox, Card, Badge, Dialog, Skeleton, Toast (sonner). Motion primitives:
150–250ms, ease-out, `prefers-reduced-motion` honoured, **nothing animates while typing**.

Phase 1's `components/ui.tsx` is a deliberate stopgap — 60 lines of hand-rolled Tailwind written to
get a login screen up. It is replaced here, not extended.

**Exit:** the theme changes from one file. No screen hardcodes a colour.

---

### 3 · App shell · ~2 days

- **Permission-driven navigation.** The nav is generated from the user's permission claims, so a
  storeman does not see an Accounts menu. Restating the Phase 1 lesson: this is *courtesy*, not
  security — the endpoint behind every link denies by default regardless. Hiding a button is not
  authorisation (ISSUES A5).
- **Company switcher** in the shell (built in Phase 1, moved here and made permanent).
- Theme toggle, user menu, sign out.
- Correlation-id-aware error boundary: an unexpected failure shows the reference id the API
  returned, because that string is the only link between "it broke" and the log line saying why.

**Exit:** a signed-in user sees only what they may use, in either theme, on a phone.

---

### 4 · DataTable & form abstractions · ~3 days

**The two components the whole app is made of.** Every list is a DataTable; every form is the same
form. Get these wrong and forty screens inherit it.

- `DataTable` (TanStack Table): sort, filter, paginate, column visibility, empty state, loading
  skeleton, row actions, **Excel export** (every legacy list has one and staff rely on it).
- `Form` (react-hook-form + zod): **one schema validates the client and types the payload**, and
  the server re-validates regardless — the server is the authority (DEVELOPMENT.md §8).
- **Field-level server errors.** The API returns `ValidationProblemDetails` keyed by field; those
  errors must land on the fields, not in a banner. Getting this right once is why the other
  thirty-nine forms will do it right.
- The **reason prompt** — Phase 1's `X-Change-Reason` — becomes a shared dialog rather than a text
  box copy-pasted onto every screen.

**Exit:** a new list screen is a column definition and a query. A new form is a zod schema.

---

### 5 · Reference CRUD screen · ~2 days

Rebuild **Users** on the abstractions above. It already exists, it is real, and it exercises
everything: a list, a create form, an edit form, a destructive action, a mandatory reason, field
errors, and permission-gated buttons.

It becomes the thing a developer copies. It is documented as such.

**Exit (the phase exit):** cloning it produces a working Customers screen in Phase 3 with no new
infrastructure.

---

### 6 · History tab · ~2 days

The +0.5 week the parent plan budgets. A reusable component reading `document_versions` and
`audit_log` (both built in Phase 1, both currently unread by anything):

- version list, side-by-side diff, "print this version", permission-gated restore.

Dropped into every document module from Phase 5 onward.

**Exit:** the History tab renders against a real audit trail — and until Phase 5 writes document
versions, that means the audit log.

---

### 7 · Line-item editor prototype · ~2 days · *risk work, not feature work*

The parent plan's own risk register: *"The cart rewrite (Phase 5) changes how staff enter documents
— prototype the line-item editor in Phase 2 and put it in front of a real user before Phase 5."*

The single biggest behavioural change in the migration is that the server-side session cart
(`addtoCart` / `cartLoad` / `removeQItem`) is deleted, and the browser holds the draft document.
This slice builds that editor as a prototype and **puts it in front of whoever actually types
invoices all day**.

It is scheduled in Phase 2 rather than Phase 5 deliberately: finding out in Phase 5 that the new
entry flow is slower than the old one costs five weeks. Finding out now costs two days.

Built, at **`/prototypes/line-items`** (visible in the nav to everyone — a person who cannot find it
cannot tell us it is wrong). It saves nothing and posts nothing: `Save invoice` shows the payload
that Phase 5's `POST /api/invoices` will have to accept, which is easier to argue about than a
screen. The item catalogue is fabricated — there is no items endpoint until Phase 3 — because what
is being tested is the *entry flow*, not the data.

What it already demonstrates, and the legacy screen cannot:

- **Zero round trips while typing.** Today every line is an `addtoCart` POST plus a `cartLoad`
  redraw. The counter on the screen shows what the draft in front of you would have cost over there.
- **A mixed-rate document.** Per-line VAT, with the summary grouped by rate. `vatper` is a display
  string for the whole invoice (B5), so this document is *not representable* in the old system.
- **Money as integers, never `double`** (B1) — `apps/web/src/lib/money.ts`, unit-tested in CI.

**Running the session:** sit with whoever types invoices all day, have them enter an invoice they
actually did this week, and time it against the old screen. The question is not whether they like it.
It is whether their hand ever goes to the mouse, and what the old screen let them do that this one
does not.

**Exit:** a real user has entered a multi-line document in it, and said what they think.
*(Not yet done — this is a human step, and it is the only thing the slice is actually for.)*

---

## What Phase 2 does NOT do

No business logic. No new endpoints. No tax, no ledger, no numbering — those are Phase 5, and this
phase must not quietly grow into them.

The one thing to guard against: a design system is the easiest place in a project to spend a month.
Two weeks, and the exit criterion is a *cloned screen*, not a component gallery.

---

## Carried forward from Phase 1

Unresolved, and still blocking a production deployment:

| Item | Why it matters |
|---|---|
| ~~**Legacy source path**~~ | ✅ **Checked, 2026-07-14 — and it was real.** The legacy app had 23 positional `INSERT INTO … VALUES (…)` statements, including 16 into `invoice_h`, `quotation_h` and `po_h`, which Phase 1's migration had *already* broken (`ERROR 1136: Column count doesn't match value count`, proven against dev). All 23 now name their columns; the legacy app compiles. **The patched legacy app must be deployed before the migrations, not after.** See [PHASE-3-PLAN.md](PHASE-3-PLAN.md). |
| **`STQ-0` duplicate** (Finding 9) | Two real quotations share a number. `quotation_h` cannot take its unique index until the business resolves it. |
| **3 orphaned payments** (Finding 10) | Cannot be attributed to a company. Surface in Data Exceptions. |
| **Data Protection keys** | Must be persisted to shared storage in production, or a redeploy invalidates every stored SMTP password. |
| **Numbering initialisation** | Run at cutover, *after* the legacy app stops issuing that document type. |
