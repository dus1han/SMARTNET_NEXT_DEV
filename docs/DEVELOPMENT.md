# Development Handbook

How this project is built, run, and worked on.
For *what* gets built and in what order, see [DEVELOPMENT-PLAN.md](DEVELOPMENT-PLAN.md).

---

## 1. Tech stack

| Layer | Choice | Version | Why |
|---|---|---|---|
| **Frontend** | Next.js (App Router) | 15 | Server components, file routing, first-class TS |
| | TypeScript | 5.x | Non-negotiable — the legacy app's bugs are largely type bugs |
| | Tailwind CSS | 4 | Utility styling; the theme lives in one place |
| | shadcn/ui | latest | Owned components, not a dependency you fight |
| | TanStack Query | 5 | Server state, caching, retries, optimistic updates |
| | TanStack Table | 8 | The app is mostly data grids |
| | react-hook-form + zod | latest | One schema validates client *and* types the payload |
| | Framer Motion | 12 | Subtle motion (150–250ms), `prefers-reduced-motion` honoured |
| | Recharts | 2 | Dashboards |
| | sonner | latest | Toasts |
| **Backend** | ASP.NET Core | .NET 9 | Team already knows C#; keeps the domain logic |
| | EF Core + Pomelo | 9 / 9 | MySQL provider; parameterised by construction |
| | FluentValidation | 11 | Server-side validation as the authority |
| | Serilog | 4 | Structured logs |
| | QuestPDF | 2026.x | PDF generation (Phase 8, deferred) |
| | ClosedXML | 0.10x | Excel exports — ports from legacy nearly as-is |
| | Argon2 (Isopoh) | latest | Password hashing |
| | Hangfire *(or hosted service)* | latest | Background jobs — email, reconciliation |
| **Database** | MariaDB | 10.11 | **Existing production server. Unchanged.** |
| **Infra** | Docker + Compose | latest | Linux target |
| | Nginx | stable | Reverse proxy; routes ported paths to new, rest to legacy |
| **CI** | GitHub Actions | — | Build, test, lint on every push |

### Deliberately *not* used
- **No ORM-free raw SQL.** Ever. EF Core or a parameterised command — nothing else.
- **No `double`/`float` for money.** `decimal` only. This is a lint-enforced rule.
- **No Crystal Reports.** Windows-only; cannot run on the Linux target.
- **No client-side-only validation.** The server is the authority.

---

## 2. Prerequisites

```powershell
# Windows dev machine
winget install Microsoft.DotNet.SDK.9      # .NET 9 (10 SDK also works)
winget install OpenJS.NodeJS.LTS           # Node 20+
winget install Docker.DockerDesktop        # Docker
winget install Git.Git
```

Verify:
```powershell
dotnet --version    # 9.x or 10.x
node --version      # v20+
docker --version
```

---

## 3. Repository layout

```
SMARTNET_NEXT_DEV/
├─ apps/
│  ├─ api/
│  │  ├─ Smartnet.Api/              # endpoints, auth, DI, middleware
│  │  ├─ Smartnet.Domain/           # entities, tax engine, numbering, ledger — NO EF, NO HTTP
│  │  ├─ Smartnet.Infrastructure/   # DbContext, EF config, repositories, interceptors
│  │  ├─ Smartnet.Documents/        # QuestPDF templates, ClosedXML exports (Phase 8)
│  │  └─ Smartnet.Tests/            # unit + integration
│  └─ web/                          # Next.js
│     ├─ app/                       # routes
│     ├─ components/                # ui/ (shadcn) + shared (DataTable, DocumentForm, HistoryTab)
│     ├─ lib/                       # api client, auth, formatting
│     └─ hooks/
├─ packages/
│  └─ api-client/                   # TS types generated from OpenAPI — never hand-written
├─ infra/
│  ├─ docker-compose.yml            # api, web, mysql(dev), nginx
│  └─ nginx/default.conf            # routing table: ported → new, everything else → legacy
├─ tools/
│  └─ DbAudit/                      # read-only production audit tool (already built)
└─ docs/
```

**Dependency rule:** `Domain` depends on nothing. `Infrastructure` depends on `Domain`.
`Api` depends on both. Never the reverse. If the tax engine needs `DbContext`, the design is wrong.

---

## 4. Configuration & secrets

**Nothing secret is ever committed.** `.gitignore` already excludes `.env*` and `*.sql`.

`.env.example` (committed, no real values):
```env
# database — the DEV copy, never production
DB_HOST=185.73.8.1
DB_NAME=smartnet_invsys_dev
DB_USER=smartnet_invsys_next
DB_PASS=

# auth
JWT_SIGNING_KEY=
JWT_ISSUER=smartnet
ACCESS_TOKEN_MINUTES=30

# app
ASPNETCORE_ENVIRONMENT=Development
NEXT_PUBLIC_API_URL=http://localhost:5080
```

Each developer copies it to `.env` and fills in the values. In production these are Docker
secrets / environment variables — **never a file in the image, never a value in source.**

> The legacy app's DB and SMTP passwords are hardcoded in its source. That is the single
> mistake this project must never repeat.

---

## 5. How to run

### Everything, in one command
```bash
cd infra
docker compose up --build
```
| Service | URL |
|---|---|
| Web (Next.js) | http://localhost:3000 |
| API (Swagger) | http://localhost:5080/swagger |
| Nginx (the real entrypoint) | http://localhost:8080 |

### Or run the two apps directly (faster inner loop)

**API:**
```bash
cd apps/api/Smartnet.Api
dotnet watch run          # http://localhost:5080, hot reload
```

**Web:**
```bash
cd apps/web
npm install
npm run dev               # http://localhost:3000, hot reload
```

### Database

The dev database lives on the host (`smartnet_invsys_dev`) — a copy of production.
**Never point at `smartnet_invsys`.** The `smartnet_invsys_next` user has no grants on it,
so a mistyped connection string fails loudly rather than damaging live data.

```bash
# apply migrations
cd apps/api
dotnet ef database update -p Smartnet.Infrastructure -s Smartnet.Api

# create a migration
dotnet ef migrations add AddAuditColumns -p Smartnet.Infrastructure -s Smartnet.Api

# scaffold entities from the existing schema (one-off, Phase 0)
dotnet ef dbcontext scaffold "$DB_CONN" Pomelo.EntityFrameworkCore.MySql \
  -o Entities --context SmartnetDbContext --no-onconfiguring --force
```

### Regenerate the TypeScript API client
Run whenever an endpoint changes. **Never hand-edit `packages/api-client`.**
```bash
cd apps/web
npm run generate:api      # reads the API's OpenAPI schema
```

### Audit the live database (read-only)
```powershell
cd tools/DbAudit
$env:SN_DB_PASS = "<password>"
dotnet run -- schema
dotnet run -- sql "SELECT ..."     # SELECT/SHOW/WITH only — writes are rejected
```

---

## 6. Everyday commands

| Task | Command |
|---|---|
| Run everything | `docker compose up` (in `infra/`) |
| API tests | `dotnet test` (in `apps/api`) |
| Web tests | `npm test` (in `apps/web`) |
| Lint / format | `dotnet format` · `npm run lint` |
| Typecheck web | `npm run typecheck` |
| Add a migration | `dotnet ef migrations add <Name> -p Smartnet.Infrastructure -s Smartnet.Api` |
| Regenerate API types | `npm run generate:api` |

---

## 7. Process

### Branching
```
main            always deployable; protected
feat/<module>   e.g. feat/settings-companies
fix/<thing>
```
Short-lived branches. Merge via PR. No direct pushes to `main`.

### Commits
Conventional commits — they generate the changelog and make history searchable:
```
feat(settings): company profile CRUD with brand colour
fix(payments): idempotency key prevents duplicate payment rows
refactor(tax): single tax engine used by all document types
```

### Pull requests
Every PR states **what changed and why**, and must pass CI. A PR that touches money,
tax, numbering, balances or permissions gets a second pair of eyes — no exceptions.

### CI gates (blocking)
1. `dotnet build` — warnings as errors
2. `dotnet test`
3. `npm run typecheck` + `npm run lint` + `npm test`
4. **No `double`/`float` in money paths** (analyzer rule)
5. **No raw SQL string concatenation** (analyzer rule)
6. Migration is additive-only (see §8)

### Definition of done
A module is done when **all** of these hold:
- [ ] Endpoints enforce permission policies (not just hiding UI)
- [ ] Server-side validation, not client-only
- [ ] Money is `decimal` throughout
- [ ] Writes are inside a transaction
- [ ] Audit rows are produced (automatic — verify they appear)
- [ ] Unit tests on the business rules
- [ ] The legacy screen for this module is removed from its menu
- [ ] Reconciliation report still clean

---

## 8. Engineering conventions

These exist because the legacy system violated each one, and we found the damage in the data.

### Confirm it against the legacy app. Every time.

**The legacy source is at `C:\Users\Saboor.a\Desktop\SMARTNET_DEV`** (not under version control —
ISSUES E1 — so back a file up before touching it).

Before designing anything, and before any migration, **read what the old app actually does** — not what
its screens imply, not what the schema suggests, and not what the module is called. The rule exists
because assuming has already cost us, twice:

- The item master looked abandoned (500 items, zero invoice lines referencing one). It is not: item
  invoices *are* raised, and the **save throws the item code away**. Reading `InvoiceController` said in
  a minute what the data could only hint at.
- `cus_m.c_form` looked like a tenant boundary. It is an *indication* of trading entity, and a company
  filter built on it would have hidden 116 customers from the people who invoice them weekly.
- The positional `INSERT INTO invoice_h VALUES (…)` was a hypothetical blocker for two phases. It was
  real, in 23 places, and Phase 1 had already broken three of them.

**The legacy app is still the live app.** Every migration must leave its writes working, and the only
way to know is to go and look.

### Money
- `decimal` in C#, `DECIMAL(18,4)` in the DB. **Never `double`, never `float`, never `varchar`.**
- Rounding is explicit and per `app_settings` (per-line vs per-document).
- Every document line stores the **tax rate that applied at save** — never re-resolved later.

### Transactions
- One business operation = one transaction. A payment writes the payment, the ledger entry and
  the audit rows **or none of them**.
- Mutating endpoints carry an **idempotency key**. A double-clicked Save must not create a
  second payment. *(This is the exact bug that produced Rs. 1.55M of duplicates.)*

### Balances
- Balances are **derived from a ledger**, never `UPDATE ... SET balance = balance - x`.
- Legacy balances enter as a single `OPENING_BALANCE` entry — see
  [LEGACY-DATA-POLICY.md](LEGACY-DATA-POLICY.md).

### Data access
- EF Core, or a parameterised command. **String-concatenated SQL is a CI failure.**
- No `DataTable`. No positional column reads.

### Schema changes
**Additive only while the legacy app is live.** Add nullable/defaulted columns, add tables,
add indexes, widen types. Never rename, retype, narrow or drop. Every legacy write must still
succeed after every migration. The retype to `DECIMAL`/`DATE` waits for Phase 9.

### API
- REST, plural nouns: `GET /api/invoices`, `POST /api/invoices`.
- Correct verbs. **No state change via GET.** (The legacy app deletes via GET.)
- Validation errors → `400` + field-level detail. Auth → `401`/`403`. Never leak an exception.
- Every response carries a correlation id; errors are logged with it. The client sees a
  generic message, never a stack trace.

### Auth & permissions
- JWT in an httpOnly, SameSite cookie.
- **Deny by default.** Every endpoint declares a policy. An endpoint with no policy fails a test.
- The 36 permission flags are claims, enforced server-side.

### Audit
- Automatic, via the EF `SaveChanges` interceptor — there is no audit code to write, and none
  to forget. See [AUDIT.md](AUDIT.md).
- The `reason` header is required on the actions listed in that doc.

### Dates
- `DateTime.UtcNow`, stored UTC, converted at the edge. Never `DateTime.Now`.

### Frontend
- Server components by default; client components only where interaction demands it.
- Every list uses the shared `DataTable`. Every document uses the shared line-item editor.
- Motion: 150–250ms, ease-out, `prefers-reduced-motion` respected, nothing animates while
  typing.

---

## 9. Testing

| Level | What | Where |
|---|---|---|
| **Unit** | Tax engine, numbering, ledger, credit limits — *the parts where a bug costs money* | `Smartnet.Tests` |
| **Integration** | Endpoints against a throwaway MySQL container (Testcontainers) | `Smartnet.Tests` |
| **Contract** | OpenAPI schema doesn't break the generated TS client | CI |
| **E2E** | Playwright: login → create invoice → take payment → check balance | `apps/web` |

**The non-negotiable test:** an invoice with mixed VAT rates, a discount and a partial payment
produces the correct total, the correct ledger and the correct audit record. That single case
covers most of what the legacy system got wrong.

---

## 10. Environments

| Env | Database | Purpose |
|---|---|---|
| Local | `smartnet_invsys_dev` on the host | Development |
| Staging | Restored copy of prod | Rehearse migrations. **Every prod migration is rehearsed here first.** |
| Production | `smartnet_invsys` | Legacy + new, side by side, behind Nginx |

**Cutover routing:** Nginx sends ported paths to the new stack, everything else to the legacy
app. One line moves a module across. One line moves it back if it goes wrong.

---

## 11. What needs to be done — Phase 0

The immediate work, in order:

- [ ] **Rotate the production DB password** *(it is in `DBConnect.cs`)*
- [ ] **Rotate the SMTP password** *(it is in `CusOutstandingController.cs`)*
- [ ] **Reset both user passwords** — 4 chars, plaintext, almost certainly `1234`
- [ ] Restrict DB network access (drop the `'%'` grant if the app server has a fixed IP)
- [ ] Scaffold the monorepo (`apps/api`, `apps/web`, `packages/api-client`)
- [ ] `docker-compose.yml` — api, web, nginx
- [ ] EF Core scaffold from `smartnet_invsys_dev` → entities
- [ ] `.env.example`, `.editorconfig`, analyzer rules (no `double` money, no raw SQL)
- [ ] GitHub Actions: build + test + lint
- [ ] Nginx routing skeleton — everything to legacy, one flag to move a path

**Exit criteria:** `docker compose up` gives a running API and web shell, talking to a dev copy
of the real schema, with CI green.

Then → **Phase 1: auth, users, settings, audit** ([DEVELOPMENT-PLAN.md](DEVELOPMENT-PLAN.md)).
