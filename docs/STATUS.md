# Where the build actually is

Verified against the code on **2026-07-20**, not against the plans. Written because the plans had
drifted: Phases 1–6 carry no completion markers at all despite being finished, Phase 7 claimed a
column drop that never happened, Phase 1 still declared an "open blocker" resolved months ago, and
several documents insist cheque printing is future work when it shipped.

**This file is the answer to "what is done and what is left."** The phase plans remain the record of
*why* each thing was built the way it was; they are no longer the record of *whether* it was.

---

## Phases

| Phase | State | Notes |
|---|---|---|
| 0 · Foundations | **Done** | Monorepo, CI, compose, EF scaffold. Deviation: nginx no longer falls back to the legacy app — this host has no legacy app. |
| 1 · Auth, users & settings | **Done**, one gap | Argon2id + hash-on-login, JWT cookie, 35 permissions → policies, user admin, settings. **`document_templates` was never built** — see Phase 8. |
| 2 · Design system & shell | **Done** | Generated client, DataTable/form abstractions, History tab, company switcher. The line-item prototype route has been removed now that Phase 5 shipped the real editor, as its own header said it should be. |
| 3 · Master data | **Done** | Customers, suppliers, items, stock, Excel export. |
| 4 · Dashboard & reports | **Done** | **10/10 reports**, plus trial balance, P&L and data exceptions. Dunning queue + `email_log` built but **sending is off** — deliberately, until the balances are corrected. |
| 5 · Documents engine | **Done** | Tax engine on `decimal`, transactional numbering, ledger-derived balances, versioning, quote → invoice. |
| 6 · Purchasing & service | **Done** | POs, supplier invoices, job cards, structured contacts. **GRN deferred** out of scope — no phase assigned. |
| 7 · Money & documents | **Done** | Receipts, cheques, expenses, document storage, notes, and slice 6 (acceptance test, reconciliation, E2E). |
| 8 · PDF templates | **Substantially done** | All six QuestPDF templates exist and **cheque printing works** — measured 7×3.5in, every print audited. Gaps: no `document_templates` configurability, and no rendering tests. |
| 9 · Cutover | **Not started** | Only artifact is `MIGRATION-DATA-CHECKS.md`. |
| GL (unnumbered) | **Done** | Chart of accounts, posting engine, backfill, trial balance, P&L. Never assigned a phase number. |

---

## What is actually pending

### 1. ~~Phase 7 slice 6~~ — done, and it found the E2E harness broken

Closed on 2026-07-20. `POST /api/dev/seed-payment` is deleted, `phase7.spec.ts` covers the exit case,
and the payments reconciliation is in `legacy-analysis/RECONCILIATION.md` (2,185 of 2,187 exact; the
two that fail are STI-38 and SNI-915, rediscovered from a different direction).

**What it turned up: `npm run e2e` had been broken since 16 July and nobody knew.** `E2EHost` runs
`--no-build`, its binary was four days stale, so it applied an *older migration set* — the schema
silently lagged the code and every save 500'd on a missing column. All 8 tests failed, including the 4
untouched ones. `npm run e2e` now builds both projects first. Three assertions in the older specs had
also drifted from the UI and were masked by the breakage.

**The lesson worth keeping:** slices 1–5 were all marked shipped while the end-to-end suite could not
have passed. A green unit suite said nothing about it, because nothing ran the harness.

### 2. ~~Test-shaped holes~~ — both closed

**HTTP-level layer added.** `Smartnet.Tests/Http/` runs the real pipeline over a real socket against a
real database (`WebApplicationFactory<Program>` + Testcontainers): deny-by-default auth, the
change-reason filter, correlation ids in header and body, error responses that leak no internals, the
CORS allow-list, the httpOnly/SameSite auth cookie, and JSON shaped as the generated client expects.
Two things worth knowing: config must be injected as **environment variables**, because `Program.cs`
reads the connection string inline before `ConfigureAppConfiguration` applies; and the fixture signs in
**once**, because a login per test trips the rate limiter — which is now pinned as its own test rather
than worked around.

**PDF rendering tests added.** All eight documents render to structurally valid PDFs, plus the edge
cases that actually break a QuestPDF layout: a description far longer than its column, a document with
no lines, every optional field null at once, and 120 lines paginating. The cheque's page size is
asserted at the measured 7×3.5in — a template that renders beautifully on A4 would be useless. They do
not claim a total lands in the right box; content streams are compressed, and that is what
`tools/PdfPreview` and a human eye are for.

723 API tests, up from 664. Still outstanding: `Smartnet.Tests/UnitTest1.cs` is the scaffold file.

### 2b. Where a document's cost comes from (changed 2026-07-20)

**Item documents derive it; service documents are asked for it.** An item line references an item and the
item master carries that item's cost, so the basis is computed. A service line has no such source.

- **Item quotations** show the derived cost **read-only** on create and edit — visible while quoting, not
  typeable, because the master is the authority.
- **Service quotations** show no cost at all when raised. The figure is entered **when the quote is
  converted to an invoice**, and the conversion is **refused without it** (400). That reverses the
  2026-07-16 "no re-entry" decision, whose premise was that the cost captured up front would carry
  through — true only if one was captured, and nothing ever required one. `cost_amount` is
  `NOT NULL DEFAULT 0`, so "carried through" meant carrying a zero, and an invoice with no cost does not
  report as incomplete: it reports as 100% margin.
- A typed **zero is accepted**; a blank is not. "This cost nothing" is a claim someone may make, but it
  has to be made.
- **Mixed documents remain legal** — parts plus labour on one repair, per Phase 5 decision B, confirmed
  with the business 2026-07-20. The item/service toggle clears the line draft, which guards against
  mixing by accident; there is deliberately **no** validator behind it, because a rule there would refuse
  an invoice for a repair that used a part.

Two consequences worth knowing before this reaches anyone:

- **Nearly every conversion on live will now demand a cost.** 2,111 of 2,113 unconverted quotations
  resolve as service.
- **The derived figure will read 0.00 until the catalogue is priced.** 0 of 500 `item_m` rows carry a
  cost — see "Price the ~500 items" in §8. The mechanism is right; the data is not there yet.

### 3. Known defects, unfixed
- **~~The cost basis ignored quantity~~ — fixed 2026-07-20.** All seven creators and editors (invoice,
  quotation, credit note, purchase order; create and edit) summed the bare per-line costs, so ten pumps
  costing 500 each recorded a basis of 500 rather than 5,000 — margin overstated by a factor of the
  quantity, everywhere margin is shown. Nothing surfaced it: cost is never posted to the ledger and never
  reconciled. Now one implementation, `DocumentCostBasis`. Two tests asserted the wrong figure and were
  pinning the bug; both now assert the right one.
- **~~A legacy quotation's `Kind` disagreed with the converter~~ — fixed 2026-07-20.** The detail endpoint
  read the legacy `it` column while the converter decides on whether line item codes still resolve in the
  item master. On live **2 unconverted quotations** say `it='ITEM'` and resolve to service — they would
  have shown no cost box and then failed conversion with a 400 nobody could clear. Both now use the
  resolve-the-code rule.
- **A mixed quotation's service cost is still not captured.** Conversion derives the basis from the item
  lines only, so the labour side of a parts-plus-labour quote contributes nothing. Pre-existing; it
  understates cost rather than inventing one. Fixing it means asking for a service cost on a mixed
  conversion and adding it to the derived total — not yet a decision the business has been asked for.
- **The audit log records EF's temporary key on Create rows** — `Id: -9223372036854775000` instead of
  the real id. Affects every entity (visible on `CustomerContact` and `UserNote`). `entity_id` is
  correct so records stay findable; only the `changes` JSON is wrong.
- **~~`audit_log` is not append-only~~ — fixed on live (2026-07-20).** The cause was a schema-wide
  `GRANT ALL PRIVILEGES ON smartnet_invsys.*`, which MySQL unions over any table-level revoke — so
  `infra/sql/audit-log-grants.sql` on its own achieved nothing, exactly as its own closing note warns.
  Verified live: after running only that script, `GRANT ALL` was still present.

  `infra/sql/narrow-app-user-grants.sh` replaces it: schema-level SELECT/INSERT (neither can rewrite
  history) plus table-level UPDATE/DELETE on every table **except** `audit_log` and
  `document_versions`. No DDL — the API does not migrate at startup, so the running app never needs it.

  Proven as the application user on live: INSERT into `audit_log` succeeds; UPDATE and DELETE are
  refused with 1142; UPDATE and DELETE on business tables still work.

  **Dev is still unfixed** — its app user holds `GRANT ALL` on `smartnet_invsys_dev` and no account
  available to us has `GRANT OPTION` there. Run the same script on that host with an admin account.
- **`/api/dashboard/analytics` and `/api/reports/companies` return 500 on dev**, apparently from
  remote-DB timeouts rather than logic.
- **~~The Documents page is blank on live~~ — fixed on live (2026-07-20).** Not a code fault:
  `documents` is a new table and the restore brought the legacy files across as `docstore` BLOBs, so
  live's library was genuinely empty. It could not be filled, because `tools/DocstoreMigrate` carried
  the same unconditional "refuse `smartnet_invsys`" guard `Program.cs` had, which inverts at cutover
  for the same reason. Now scoped — `--production` plus an explicit `--root` are required together.

  **All 18 materialised onto `/var/www/sys-documents` and verified**: 18 of 18 re-read and re-hashed
  against their recorded SHA-256, and the API container reads them at its configured root. Stamped
  `company_id = 1`; that is a label for which entity issued it, **not** a visibility boundary — see
  `ICompanyAccessService`, every user is granted every company. `docstore.pdfdoc` is untouched, so
  this is still reversible.
- **~~Uploads were silently broken on live~~ — fixed on live (2026-07-20).** Found while doing the
  above. `/var/www/sys-documents` was `root:root 755`, and the API runs as uid 1654 (`USER $APP_UID`);
  a bind mount keeps the host's ownership. Root-owned it stays world-*readable*, so nothing looked
  wrong — and every upload would have failed on permission denied. `chown -R 1654:1654` fixed it, and
  the compose file now says so. Re-apply the chown after any root-run tooling touches that directory.

### 4. Legacy data — decisions, not code

**Dev is at 8 exceptions, down from 155.** Four were decided and worked to zero on 2026-07-20; every
one still has to be repeated on live, and the scripts are in `infra/sql/`. Full detail, including the
checks made before each, is in [MIGRATION-DATA-CHECKS.md](MIGRATION-DATA-CHECKS.md).

| Fixed on dev | How | Live |
|---|---|---|
| 105 orphaned line groups (690 rows) | `remove-orphaned-lines.sql`, archived first | **to run** |
| 38 supplier paid-not-settled | `settle-supplier-paid-not-settled.sql`, rows marked `RECONSTRUCTED` | **to run** |
| 3 payments naming no invoice (70,381) | `remove-orphaned-payments.sql`, archived with their GL | **to run** |
| STI-1068 paid, no payment (21,500) | The app's audited correction, not a script | **to do** |

**The eight that remain**, none of which is a script's job:

- **`STQ-0` — the only one still blocking cutover.** Two quotations share the number, so the unique
  index cannot be built on `quotation_h`. Somebody holds a printed PDF with it on, and renumbering to
  make an index build is the historical rewriting LEGACY-DATA-POLICY forbids. Business decision.
- **2 overpaid** — STI-38 (71,000 paid twice, seventeen days apart) and SNI-915 (23,600, three weeks
  apart). Either void the payment that should not be there, or leave the customer in credit — both are
  legitimate; zeroing a balance is not.
- **4 lines ≠ header** — STI-1150 is the serious one (header 12,041 against 1,916 of lines); the other
  three are gaps of 700, 240 and 60. Each needs a per-invoice decision about which side is right.
- **1 supplier settled twice** — invoice 9784, 165,000 recorded as settled twice, each settlement
  standing for the whole invoice.

And this, which is easy to miss: **a clean duplicate-payment count does not mean nothing is
overpaid.** The remediation matched same invoice + same amount + *same date*; STI-38 and SNI-915 are
dated weeks apart, survived it, and are the two still on the list. Check the Overpaid tile as well.

### 5. Security items outstanding
- **The legacy `notes` table is a plaintext credential store** — banking, email, iCloud and
  third-party logins, still being appended to as of July 2026. It must not be carried into the Legacy
  Archive as it stands. See `MIGRATION-DATA-CHECKS.md`.
- **Plaintext `password` column is still live** and still written on password change — correct for
  the dual-write window, dropped at cutover.
- ~~**Data Protection keys are not persisted.**~~ **Fixed 2026-07-21.** Carried unresolved since Phase 1,
  and it finally cost an afternoon. The key ring lived in the container's home directory, so each redeploy
  minted a new one and every ciphertext already in the database became undecryptable — while still sitting
  there looking perfectly intact. The FTP password decrypted to an empty string, the login was refused, and
  `FtpBackupStorage` reported that as *"could not reach the backup server"*: hours went into traceroutes,
  firewall rules and the FTP provider before anyone suspected encryption. (A real ban by the FTP host had
  lifted hours earlier, which is what made the wrong trail so convincing.)
  - The ring is now written to `DataProtection__KeyPath`, mounted from `/var/www/sys-dpkeys`, and
    `Program.cs` **refuses to start** without it — this class of fault is invisible until it is expensive.
  - **These files decrypt every stored password. Back them up; losing them means re-entering all of them.**
  - The four SMTP call sites let the decryption failure propagate, so document email had been returning 500s
    since the redeploy rather than misreporting. Same root cause, same fix.
  - Decryption failure and a rejected login are now separate exceptions, as is a rejected TLS certificate.
    Collapsing all three into one message is what made this take as long as it did.

### 5b. Settings — companies and VAT (2026-07-20, VAT model reworked 2026-07-21)

- **Adding a company is possible, and it provisions.** `POST /api/companies`, **Dev_Admin only** — the
  first endpoint in the codebase actually gated on `system.dev_admin`, which until now existed only to
  satisfy every other policy implicitly. It writes the company, its tax rates, **9 numbering series** and
  **5 email templates** in one transaction. That list is not padding: the two original companies were
  provisioned by a migration that cross-joined `FROM companies_m` over the companies present when it ran,
  nothing re-runs it, and a bare row is a company that cannot raise any document (`TaxEngine` throws with
  no default rate in force) and cannot email one — with no way to fix the templates from the UI, because
  that API reads and updates but has no insert. **The create form asks only for the VAT tick** — a
  VAT-registered company inherits the rate the others charge today; an unregistered one gets a zero rate
  only.
- **VAT is a business-wide rate, not a per-company one.** `POST /api/settings/vat-rate` (Dev_Admin) sets
  it for **every VAT-registered company at once**, as their default from a valid-from date. Per company,
  only the *adoption date* is editable (`PUT /api/settings/tax-rates/{id}`), for the case where one
  entity switched over on a different day. The rate and percentage are the same everywhere by
  construction.
- **Both live under Administration, on their own screens** — `/companies` and `/vat-rate`, Dev_Admin
  only. They were briefly buttons on the Settings page and that was the wrong home: Settings configures
  *one* company at a time (letterhead, numbering, mail), while adding an entity changes what the business
  is and the VAT rate applies across all of them. Settings keeps the per-company tax table, where the
  only editable field is that company's adoption date.
- **Scheduling works because default-clearing keys on the start date, not overlap.** Two defaults with
  different start dates coexist; the engine resolves each document against the default with the latest
  start on or before its date, so "18% now, 20% from January" needs no end date on the 18% and no
  clearing of its flag. Only a default on the *same* start date supersedes. The earlier overlap-based
  clearing would have felled live's open-ended 18% the moment a future rate was added — that is fixed.
- **Non-VAT companies show only a zero rate.** Smart Technologies is flagged not-VAT-registered but the
  seed had given it a "VAT 18%" default (it seeded blind to the flag); the engine forced 0% anyway, so
  taxation was right while the data lied. Cleaned on live 2026-07-21 (`infra/sql/tax-rates-non-vat-zero-only.sql`):
  ST now carries the zero rate as its default and the stray 18% is soft-deleted. **ST shows 0 only, SN
  shows both**, which is what the business asked for.
- Company **delete/deactivate is still not implemented** — `deleted_at` exists and is filtered on, but
  nothing sets it.

### 5c. Database backups (2026-07-21)

**Hourly to FTPS, newest 15 kept, restore on demand.** Administration → Backups, Dev_Admin only.

- **The destination is configured on the screen**, not in a deploy — `backup_settings`, with the FTP
  password encrypted at rest and write-only over the API, the same contract as the SMTP password.
- **The dump uses the application's own credentials.** Verified against live: 82 tables, 21 MB raw →
  **9.9 MB gzipped**, so 15 backups is ~150 MB. `mysqldump` needs no more than the SELECT / LOCK TABLES /
  SHOW VIEW the app already holds — except `--events`, which needs a privilege it does not have and
  which this schema has no use for (zero events, zero triggers, zero routines).
- **Restore needs a second, privileged credential** — `Backup__RestoreConnectionString`, deliberately in
  the server env and not on the screen. Create it with `infra/sql/backup-restore-user.sql`. **Unset, and
  restore reports itself unavailable while backups and downloads work.**
- **A restore takes a safety copy first**, into a separate folder that the rotation never prunes, and
  requires a typed `RESTORE` plus a change reason.
- `mariadb-client` is now in the API image — the dump shells out to `mysqldump` rather than
  re-implementing it.

**Two things to be clear-eyed about**, both inherent to the feature rather than defects in it:

1. **A restore overwrites `audit_log`**, which was deliberately made append-only so the application
   could not rewrite history. It is the one action in the system that can erase the record of itself.
   The restore is logged at Warning to the container log, which it cannot reach.
2. **Giving the API a DDL credential means a compromise of the API is a compromise of every record.**
   That trade was made knowingly so the restore button could exist. Unsetting the variable withdraws it.

**Not yet verified end to end**: there is no FTP destination to test against, and no restore has been
run. The retention and naming rules are unit-tested (20 tests); the FTP round trip and the restore path
are unproven until pointed at a real server.

### 5d. Draft autosave on the four create screens (2026-07-22)

**Quotation, invoice, purchase order and job card keep their work as you type**, on the server, so a
closed tab, a signed-out session or a stray reload no longer costs somebody a forty-line document. Each
list grew a **Drafts** tab beside its issued documents; a draft is resumed by opening it and discarded
from either the tab or the create screen.

- **A draft is not a document.** New table `document_drafts` — *not* a status column on `quotation_h` /
  `invoice_h` / `po_h` / `jobs_m`. Two reasons, neither cosmetic: the legacy app still reads those tables
  and has no notion of a draft, so a half-typed invoice would show up in its lists and reports; and a
  number is allocated inside the save transaction, so a draft holding one would either reserve it for as
  long as a tab stayed open or leave it blank and collide with the unique index the cutover is building.
  A draft takes no number, posts no ledger and moves no stock. Raising it goes through the ordinary
  create endpoint, unchanged.
- **The payload is opaque to the server** — the create screen's own state, stored verbatim, checked only
  for being well-formed JSON. It carries a shape version, and a payload from a shape the screen no longer
  understands is refused rather than half-restored.
- **Not `IAuditable`, deliberately.** The interceptor writes an `audit_log` diff per save, and autosave
  saves every few seconds; a single invoice would leave hundreds of diffs of a JSON blob. There is a test
  that fails if somebody adds the interface back.
- **Shared within the company, not private.** Whoever may raise the document may resume its draft — one
  person books the work, another prices it. `row_version` keeps them apart: a stale autosave is a 409 and
  the screen says so, rather than silently overwriting the other person.
- **Gated per draft type, in code**, because one controller serves four document types with four
  different create permissions. `DocumentDraftPermissionTests` reads each permission off the create
  endpoint by reflection, so the two cannot drift.
- **Hard-deleted**, unlike everything else in the schema — a raised draft is superseded by an audited
  document, and a discarded one is something somebody decided not to keep.

**Not covered**: the edit screens (`/[id]/edit`) do not autosave — an edit still has the saved document
to fall back on. There is no expiry, so an abandoned draft sits in the list until somebody discards it.

### 6. Phase 8 gaps
- **`document_templates` does not exist.** Promised in both Phase 1 and Phase 8; templates are driven
  by `Company` alone. There is no per-company template settings surface.
- **Whether the cheque overlay registers on physical stationery is unverified in the repo.** The page
  size is measured, but only a real cheque through a real printer settles it.

### 7. Phase 9 — all of it
Drop the plaintext password column · `varchar → DECIMAL/DATE` retype · drop `docstore.pdfdoc`
(bytes are on disk **on dev only** — live must be materialised first; C4's bloat reclamation is
blocked on this) · remove the Crystal and
legacy dual-writes · delete `DBConnect` · archive the legacy app · numbering initialisation ·
work `MIGRATION-DATA-CHECKS.md` to zero.

### 8. Deferred with no home
- **GRN (goods received note)** — deferred out of Phase 6, never assigned a phase. Until it exists a
  PO posts no stock and all stock-in goes through a manual adjustment.
- **Turn dunning on**, once balances are trusted.
- **`LegacySchema.cs` needs a real `mysqldump --no-data` baseline** — its own comment says "before
  staging".
- **`documents.entity_type` is dead** — every row is NULL, no caller sets it, and its doc comment
  describes a lowercase vocabulary that conflicts with the PascalCase one `audit_log` and the notes
  module use. Wire it up or delete it; a column that is always NULL with two competing vocabularies
  is worse than neither.
- ~~**Tax rates are read-only in the UI.**~~ Done 2026-07-20 — see §5b.
- **Price the ~500 items**, and explain `c_form`.
- **Per-customer profit bands drive auto-pricing in the legacy app, and are not ported.** Read from the
  legacy source on 2026-07-21; written down here because the data survives and the behaviour does not.

  `profit_percent` is a lookup of ten margin bands (5, 10, 15 … 50). Each customer carries one in
  `cus_m.pro`, and it is **not stale configuration** — all **224 of 224** customers have one set: 114 at
  25%, 68 at 10%, 20 at 20%, 19 at 15%, 3 at 5%. That is a maintained commercial policy separating trade
  from retail.

  Two distinct pieces, which are easy to conflate:

  1. `Invoice/getCustomerProfit` returns *all ten bands* and only ever fills the band dropdown on the
     **add-customer form** (`#AddCProit`). It assigns a band; it prices nothing.
  2. `ItemQuotation/getprofitpercentage` returns *one customer's* band, and the item-quotation screen
     auto-fills the rate when a line item is picked
     (`Scripts/ControllerJS/ItemQuotationJs.js`, and the same in `EditItemQuotationJs.js`):

     ```js
     var sellprice = parseFloat(data.Cost) + (parseFloat(data.Cost) * (parseFloat(profitpercentage) / 100));
     $('#Qrate').val(Math.ceil(sellprice));
     ```

     So **`price = ceil(cost × (1 + band%))`**, written into the rate box and overtypable. The
     `Math.ceil` is a real convention — it always rounds *up* to the whole rupee — and would be missed
     if this were rebuilt from the formula alone.

  **The asymmetry matters:** the item *invoice* screen does not do this. Same grid, same click, and
  `$('#invrate').val('')` — the rate is typed by hand. Auto-pricing exists on item quotations only,
  never on invoices or service quotations.

  Nothing is lost by leaving it: `Customer.ProfitPercentId` is already carried on the new entity, so the
  bands survive. It is **blocked on item costs** regardless — every `item_m.cost` is null, so building it
  today would suggest a price of zero. Sequence it after pricing the catalogue, which also activates the
  read-only cost figure on item quotations (§2b).
- **The legacy Web Catalogue was never ported, and it is not empty.** `WCategory`/`WProducts`, which
  `legacy-analysis/FUNCTIONS.md` marks "possibly dead code — confirm before porting". Measured on live
  2026-07-21: **`wb_products` 111 rows, `wb_prod_cat` 22, `wb_prod_seq` 117, `wb_projects` 0.** So the
  question is still open rather than answered by absence — and if it feeds anything customer-facing it
  disappears when the legacy app is archived. A business decision, not a technical one.

---

## Suggested order

1. **Fix the `audit_log` grants** — dev is confirmed broken, production is unknown. Needs a DB admin,
   takes minutes, and until it is done the audit trail is not evidence.
2. **Phase 7 slice 6.** One task closes the highest-severity item (`seed-payment`) and the largest
   missing test.
3. **Get the five legacy-data decisions answered** — the orphaned lines block foreign keys, which is
   structural and gets more expensive the longer it waits.
4. **Add a `WebApplicationFactory` fixture.**
5. **Fix the audit interceptor's temporary-key bug.**
6. Then Phase 9.
