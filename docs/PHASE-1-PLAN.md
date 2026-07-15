# Phase 1 — Auth, users, roles & settings

The security fix. ~4 weeks. Delivered as **vertical slices**: each one ends with something
you can log into and click, not a pile of untested endpoints.

Parent: [DEVELOPMENT-PLAN.md](DEVELOPMENT-PLAN.md) · Audit spec: [AUDIT.md](AUDIT.md)

---

## Amendment to the parent plan — roles (2026-07-14)

The parent plan mapped the 36 flat `user_permissions` flags straight to claims. That is not
enough: **Dev_Admin and Company Admin are different kinds of administrator**, and new roles
will be added as development continues. So Phase 1 introduces a role layer.

| Role | Scope | Meaning |
|---|---|---|
| `Dev_Admin` | **Cross-company**, system | The developer/superuser. Bypasses company scoping. Can see the audit log, the data-exceptions screen, every company. Not a business user. |
| `Company_Admin` | One company | Full business permissions **within their company**. Manages users, settings, tax, numbering for that company. Cannot cross into another company or reach dev-only surfaces. |
| *(business roles)* | One company | e.g. Sales, Accounts, Store — named bundles of the 36 flags. Added as needed without a deployment. |

Model:

```
roles                    (id, company_id NULL=global, name, is_system)
role_permissions         (role_id, permission_key, granted)
user_roles               (user_id, role_id, company_id)
user_permission_overrides(user_id, permission_key, granted)   -- exception, not the norm
```

**Effective permission = role grants, then per-user overrides applied on top.** Overrides exist
because there is always one person who needs one extra thing; they are visible and audited.

⚠️ **The legacy app is still live and still reads `user_permissions`.** So the new model
**writes through** to that table: whenever effective permissions change, the 36 flags are
recomputed and written back. Legacy keeps working; the new app is the source of truth. The
write-through is deleted in Phase 9 with the rest of the legacy app.

`is_system` roles (`Dev_Admin`, `Company_Admin`) cannot be deleted or have their permission set
emptied through the UI — otherwise the first misclick locks everyone out of the admin screens.

---

## Slice 1 — The audit spine · ~1 week

Nothing else may be built first. Every entity written after this point inherits audit for free;
anything written before it has to be retrofitted.

- `SmartnetDbContext` — the app-owned context that carries migrations, alongside the Phase 0
  scaffolded `SmartnetLegacyDbContext` (which stays read-only reference until it is retired).
- **Baseline migration** — the existing schema, captured as migration 1 without re-creating it.
- **Audit migration** — audit columns + `row_version` added *additively* to every table;
  `audit_log`; `document_versions`. Additive only: the legacy app must still write successfully
  after every migration (see DEVELOPMENT.md §8).
- **`AuditSaveChangesInterceptor`** — diffs the change tracker, writes `audit_log` inside the
  same transaction as the business change. There is no audit code for a developer to forget.
- **Ambient change context** — `X-Change-Reason` header → scoped context the interceptor reads.
  Endpoints declaring a mandatory reason reject the request without one, server-side.
- **Redaction list** — `password_hash`, `password_encrypted` and friends are recorded as
  *changed*, never with their values.
- **Correlation id + global exception handler** — generic ProblemDetails to the client, full
  detail and correlation id to the structured log. Closes A9.
- **Append-only GRANT** — the app's DB user gets `INSERT`/`SELECT` on `audit_log`, never
  `UPDATE`/`DELETE`. Checked in as SQL, applied to dev and prod.

**Exit:** a test proves that a business change and its audit row commit together or roll back
together, that a redacted field never reaches the log, and that a reason-required endpoint 400s
without the header.

---

## Slice 2 — Auth · ~1 week

- Additive columns on `user_m`: `password_hash`, `password_changed_at`, `must_change_password`,
  `failed_login_count`, `locked_until`. The plaintext `password` column **stays** until Phase 9,
  because the legacy app still reads it.
- **Argon2id** (Isopoh). Login: if a hash exists, verify it; if not, fall back to the legacy
  plaintext comparison and **write the hash on success**. Both apps keep working through cutover.
- Anyone still on `1234` is forced to change on next login.
- **JWT in an httpOnly, SameSite cookie.** Short-lived access token, sliding refresh.
- Login rate limiting + account lockout. **Login is audited — success *and* failure.** (Today
  neither is recorded.)
- `POST /api/auth/login` · `/logout` · `/change-password` · `GET /api/auth/me`.
- Frontend: Tailwind 4 + shadcn + TanStack Query bootstrapped (pulling the minimum of Phase 2
  forward), login page, forced-change-password page, route guard.

**Exit:** you log in with a real account, the plaintext password is silently upgraded to a hash,
and both the success and a deliberate failure appear in `audit_log`.

---

## Slice 3 — Roles, permissions & users · ~1 week

- Migration: the four tables above. Seed `Dev_Admin` and `Company_Admin`. Migrate every existing
  user's 36 flags into a generated role so nobody's access changes on day one.
- Write-through projection back to legacy `user_permissions`.
- Effective permissions → JWT claims → **ASP.NET Core policies on every endpoint. Deny by
  default.** A test enumerates every endpoint and **fails the build** if one carries no policy —
  that is what stops A5 from creeping back.
- Endpoints: users (list, create, edit, disable, reset password, assign role), roles (CRUD).
  **Reason is mandatory** on permission changes and password resets.
- Frontend: app shell with permission-driven navigation, user admin, role editor.

**Exit:** a non-admin calling an admin endpoint directly (not just having the button hidden)
gets a 403.

> **Amended 2026-07-14.** This slice originally also promised: *"a `Company_Admin` of company A
> cannot see company B."* **The business does not want that, and it was never true here.** Smart Net
> and Smart Technologies are two trading *entities*, not two tenants: the same staff raise documents
> for both, every day, and there are no per-company users. Every user sees every company.
>
> The company on a document says which entity issued it — which letterhead, which numbering, which
> VAT treatment. The switcher in the shell chooses which entity you are issuing under. **It is not an
> authorisation boundary, and nothing may be built as though it were.** The per-company mechanism
> stays (see `CompanyAccessService`) because that is where a boundary would go if an entity is ever
> acquired whose books the existing staff should not see — but it is dormant, and every role
> assignment today is global.

---

## Slice 4 — Settings & multi-company · ~1 week

- `company_id` added to every document table — nullable + backfilled + indexed, so the legacy
  app's inserts still succeed. **This is the decision most expensive to reverse; it lands now.**
- Companies extended: VAT-registered flag, branding, bank details, document header values.
- New settings tables: `document_series` (numbering), tax rates, `document_templates`,
  `mail_settings` (**encrypted at rest, write-only over the API — a GET never returns the
  password**, plus a *send test email* action — closes A2), `email_templates` (closes A2b),
  `app_settings` for the seven business rules from the parent plan §Settings surface.
- Company scoping: an active-company claim + an EF global query filter on documents.
  `Dev_Admin` bypasses it.
- Frontend: the settings area, and the company switcher in the shell.

**Exit (the phase exit):** an admin changes the company header, the VAT rate, the invoice prefix,
the credit-limit policy and the SMTP settings **from the UI, with no deployment** — and every one
of those changes is in the audit log with a reason attached.

---

## What Phase 1 closes

A1 A2 A2b A2c A3 A4 A5 A6 A7 A9 A10 · B7 B8 · E2 E4 · F2 — and the silent-overwrite
concurrency hole.

## Open blocker

`ConnectionStrings__Smartnet` is absent from `.env`. Migrations cannot run without a reachable
`smartnet_invsys_dev`. `.env.example` has the key; it needs filling in locally.
