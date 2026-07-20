# SMARTNET_NEXT

Rebuild of Smart_InvSys (ASP.NET MVC 5 / Crystal Reports / raw-SQL MySQL) as:

- **Frontend** — Next.js 15 (App Router, TypeScript, Tailwind, shadcn/ui, Framer Motion)
- **Backend** — ASP.NET Core 9 Web API (EF Core + Pomelo MySQL)
- **Database** — the existing `smartnet_invsys` MySQL schema, unchanged at the start
- **Hosting** — Linux + Docker, Nginx reverse proxy
- **Strategy** — strangler: old and new run side by side against the same DB; modules cut over one at a time

## Layout

```
apps/
  api/        ASP.NET Core 9 — Api / Domain / Infrastructure / Documents
  web/        Next.js 15
packages/
  api-client/ TypeScript types generated from the API's OpenAPI schema
infra/
  docker/     compose files
  nginx/      routes ported paths to the new stack, everything else to legacy
docs/
  legacy-analysis/   what the old system does, and what's wrong with it
```

## Docs

**Start here: [docs/STATUS.md](docs/STATUS.md)** — what is built, what is pending, verified against
the code rather than the plans. The phase plans record *why* each thing was built; STATUS records
*whether* it was.

| File | What it is |
|---|---|
| [docs/STATUS.md](docs/STATUS.md) | Current build state and the outstanding work, per phase |
| [docs/legacy-analysis/FUNCTIONS.md](docs/legacy-analysis/FUNCTIONS.md) | Every function in the old app — 248 actions across 44 controllers, grouped into modules |
| [docs/legacy-analysis/ISSUES.md](docs/legacy-analysis/ISSUES.md) | 34 defects that must not be carried across, with severity and the standard to meet |
| [docs/legacy-analysis/MIGRATION.md](docs/legacy-analysis/MIGRATION.md) | Target architecture, the settings model, phased delivery plan |
| [docs/legacy-analysis/REPORT-FIELDS.md](docs/legacy-analysis/REPORT-FIELDS.md) | Field contracts for the 12 Crystal reports (PDF work is deferred to the end) |
| [docs/legacy-analysis/rpt/](docs/legacy-analysis/rpt/) | Machine-extracted report definitions + the extraction tool |

## Do first

1. **Rotate the MySQL password** — it is hardcoded in the old `DBConnect.cs` and has been
   distributed with every copy of that folder.
2. **Rotate the SMTP password** — hardcoded in the old `CusOutstandingController.cs`.
3. Restrict database network access.
4. Take a schema dump and build a **dev database**. Never develop against production.
