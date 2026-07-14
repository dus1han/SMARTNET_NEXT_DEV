# Production-Standard Defect List

Everything in the current app that must **not** be carried across as-is. Each item
states the current practice, the evidence, and the standard the rebuild must meet.

Severity: 🔴 must fix before/at cutover · 🟠 fix during rebuild · 🟡 cleanup

---

## A. Security

### A1 🔴 Hardcoded production database credentials
`DBConnect.cs:28-34` — server IP, username and password in source. `Web.config` has no
connection string at all. Shipped with every copy of the folder.
**Standard:** environment variables / Docker secrets. **Rotate the password now** — it is
already exposed.

### A2 🔴 Hardcoded SMTP credentials — *and they reuse the DB password pattern*
`CusOutstandingController.cs:157` and again at `:304`:
`new NetworkCredential("info@smart-net.lk", "Admin@2023##")`, host `mail.smart-net.lk`.
Duplicated in at least two places.
**Rotate this password too.**

**Standard:** a **`mail_settings` table, per company** (host, port, SSL, username,
`password_encrypted`, from/reply-to/bcc, send kill-switch, daily limit) — see `MIGRATION.md §2`.
The password is **encrypted at rest and write-only over the API**: the settings UI shows
`••••••` and a GET never returns it. Ship a **"Send test email"** action, or misconfiguration
is only discovered when a customer doesn't receive their invoice.

### A2b 🟠 Email subjects and bodies are hardcoded too
Five mail flows (`emailIPDF`, `emailQPDF`, `emailPOPDF`, `emailOS`, `emailOSBulk`) each have
their subject and body written into C#. Finance cannot reword a dunning letter without a
deployment.
**Standard:** an `email_templates` table keyed by company + template, with token substitution
(`{{invoice_no}}`, `{{customer_name}}`, `{{total}}`, `{{due_date}}`).

### A2c 🟠 No record of what was sent
Nothing logs outbound mail, so "did the customer actually receive their invoice?" is
currently **unanswerable**.
**Standard:** an `email_log` (recipient, template, document ref, status, error, sent_at).

### A3 🔴 SQL injection — 346 call sites, 43 of 44 controllers, zero parameterized queries
Reaches the **unauthenticated** login endpoint (`LoginController.cs:55`).
**Standard:** EF Core / parameterized commands only. No string-concatenated SQL, ever.

### A4 🔴 Plaintext passwords
Compared directly (`LoginController.cs:77`), written raw on change (`:24`), new users get
a hardcoded `1234` (`ManageUserController.cs:173`).
**Standard:** Argon2id or bcrypt; forced change on first login; no default passwords.

### A5 🔴 Authorization is cosmetic
`SessionExpireAttribute` only checks `Session["user"] != null`. Permission flags are read
in `Index` actions to hide UI; the data endpoints check **nothing** —
`ManageUserController.getUserPer` / `updatepermission` are callable by any logged-in user,
including a customer-type account. **This is privilege escalation to admin.**
**Standard:** per-endpoint policy authorization from claims. Deny by default.

### A6 🔴 IDOR — no ownership checks
The `setselected*` actions (~8) stash a record ID in session; nothing verifies the user may
see that record. Any authenticated user can read/print any invoice, quotation or job card.
**Standard:** every record fetch validates tenant/customer scope against the caller's claims.

### A7 🟠 No CSRF protection, and state changes accept GET
No `[ValidateAntiForgeryToken]` anywhere; no `[HttpPost]` on any action — deletes and
saves are reachable by GET.
**Standard:** correct HTTP verbs; SameSite cookies; anti-forgery on cookie-auth mutations.

### A8 🟠 Unrestricted file upload
`DocStoreController.cs:98-112` — accepts any file, any size, any extension, using the
client-supplied filename, written under the web root. No whitelist, no MIME check, no size cap.
Same pattern in `WProductsController.cs:62`.
**Standard:** extension + content-type whitelist, size limit, server-generated filenames,
storage outside the web root (object storage).

### A9 🟠 Exception details returned to the browser
Every controller: `catch (Exception ex) { return Json(new { data = "terror", te = ex.ToString() }) }`
— leaks stack traces, SQL, and schema to the client.
**Standard:** generic client error + correlation id; full detail to structured logs only.

### A10 🟠 `debug="true"` in Web.config, 500-minute sessions
`Web.config:22`, `:24`.
**Standard:** debug off in production; short sliding session/token expiry.

---

## B. Data integrity & correctness — *the most dangerous category*

### B1 🔴 Money is stored and calculated as `double`
`AdminDashboardController.cs:102`, `CNoteController.cs:86`, `ChequeController.cs:110`,
and `Convert.ToDouble(...)` throughout. **There is not one `decimal` in the entire
controller layer.** Binary floating point cannot represent 0.1 exactly; totals, VAT and
balances drift. On a cheque printer and a VAT return, that is a real defect, not a
theoretical one.
**Standard:** `decimal` end to end; MySQL `DECIMAL(18,4)`; explicit rounding rules.

### B2 🔴 No transactions anywhere
`BeginTransaction` appears **zero** times. `DBConnect` opens and closes a connection per
statement. Multi-statement business operations are therefore **non-atomic**:
- `CNoteController.cs:107-109` — insert credit note, insert payment, update invoice balance:
  three separate statements. A failure between them corrupts the ledger.
- `CusPaymentsController.cs:43` / `:67` — `UPDATE invoice_h SET balance=balance±'amount'`
  on payment save/delete, unguarded.
**Standard:** one transaction per business operation; commit or roll back as a unit.

### B3 🔴 Account balances are mutated in place, with no ledger
`balance = balance ± amount` (`CusPaymentsController.cs:43,67`, `CNoteController.cs:109`).
A lost update, a double-submitted form, or a failed mid-sequence write silently produces a
wrong customer balance with **no audit trail to reconstruct it**.
**Standard:** balance is *derived* from an immutable transaction ledger, not stored and
adjusted. If stored, it's a cache rebuildable from the ledger.

### B4 🔴 Document numbering is not concurrency-safe
Numbers are checked with `checkavail("select * from invoice_h where invoiceno='…'")` and
assigned outside any transaction (`CNoteController.cs:264`). Two users saving at the same
moment can take the same invoice number.
**Standard:** allocate from a `document_series` row inside the transaction
(`SELECT … FOR UPDATE`), or a DB sequence.

### B5 🟠 VAT cannot express mixed rates
`vatper` is pushed as a *display string* (`"VAT(5%)"`) and one rate applies to a whole
document. A zero-rated or exempt line alongside a standard-rated one **cannot be
represented** — the invoice would be wrong.
**Standard:** tax rate per line, snapshotted at save; VAT summary grouped by rate.

### B6 🟠 Tax rates are not snapshotted onto documents
`vat_validity` is date-ranged and resolved at render time. Change the VAT rate and
**historical invoices silently reprint with the new figure**. That is an audit failure.
**Standard:** persist `tax_code` + `tax_rate_applied` on every document line at save.

### B7 🟠 No server-side validation
Validation is client-side jQuery only; the API trusts whatever arrives.
**Standard:** server-side validation (FluentValidation / zod on both sides) as the
authority; the client's copy is a convenience.

### B8 🟠 `DateTime.Now` (server-local) written straight to the DB
Used pervasively for `entereddt`, `printeddt`, etc. No UTC, no timezone handling.
**Standard:** store UTC (`DateTime.UtcNow` / `timestamptz` equivalent); convert at the edge.

---

## C. Reliability & resources

### C1 🟠 Connections and readers are never disposed
`DBConnect` is not `IDisposable`; `MySqlDataReader` is opened and left unclosed
(`DBConnect.cs:130`, `:163` — note the commented-out `rdr.Close()`). `checkavail` will
`NullReferenceException` if the connection fails to open.
**Standard:** `using` / DI-scoped `DbContext`; pooled connections.

### C2 🟠 Writes and mutations use `ExecuteReader`, not `ExecuteNonQuery`
`DBConnect.cs:105`, `:116` — inserts/updates run through `ExecuteReader`, leaving a reader
open on the connection.
**Standard:** correct command execution; check affected rows.

### C3 🟠 Generated files accumulate on disk forever
PDFs and Excel exports are written to `~/Files/{module}/{guid}/…` (`Server.MapPath`) and
never cleaned up (`CNoteController.cs:158`, `ChequeController.cs:148`,
`CusOutstandingController.cs:124`). Unbounded disk growth, inside the web root.
**Standard:** stream the response, or object storage with a lifecycle policy. Never serve
generated files from a guessable path under the web root.

### C4 🟠 Uploaded documents stored as BLOBs in MySQL
`docstore.pdfdoc` (`DBConnect.addpdf`). Bloats the database, slows backups, complicates
replication.
**Standard:** object storage (S3/MinIO); keep a key + metadata in the DB.

### C5 🟡 900-second command timeout, 1,000,000 ms SMTP timeout
`DBConnect.cs:37`, `CusOutstandingController.cs:161`. A 15-minute query timeout hides
missing indexes; a 16-minute mail timeout blocks a request thread.
**Standard:** short timeouts, async I/O, background jobs for email.

### C6 🟠 Bulk email sent synchronously inside a web request
`emailOSBulk` loops customers, builds an Excel file, and sends SMTP mail in-request.
**Standard:** queue it (background worker / Hangfire / hosted service) and report progress.

---

## D. Architecture & maintainability

### D1 🟠 No domain model at all
`Models/` is **empty**. Data moves as `DataTable` read positionally
(`row.Field<string>(17)`) — column order is load-bearing, so any schema change silently
corrupts reads. DTO classes are declared *inside* controllers (`LoginController.userper`).
**Standard:** typed entities + DTOs; EF Core scaffold from the existing schema.

### D2 🟠 Business logic lives in controllers, duplicated
Tax math, totals and document assembly are re-implemented in ~15 controllers.
`getCompanyData` exists in 6.
**Standard:** a domain layer (tax engine, numbering, document service) called by thin endpoints.

### D3 🟠 Four controllers per document type
Item / Service / EditItem / EditService for quotations and invoices → collapse to one
module with a line-type field. This is where the 248 actions shrink.

### D4 🟠 Server-session "cart" for line items
`addtoCart` / `removeQItem` / `cartLoad` build documents in session before saving.
Cannot survive a stateless API; also breaks under multiple tabs and load balancing today.
**Standard:** client holds the draft; post the document as one payload.

### D5 🟡 Copy-paste JS front end
43 near-identical files in `Scripts/ControllerJS`; Bootstrap 3 and jQuery 3.4.1.
**Standard:** component library, shared data-table/form abstractions.

---

## E. Process & operations

### E1 🔴 Not under source control
No git. No history, no review, no rollback. `Reports_BK/`, `Crystal Reports Backup Files/`,
`.gitconfig.swp` and a `page_bk.jsx` in the home folder are the backup strategy.
**Standard:** git, branches, PR review.

### E2 🟠 No logging, monitoring or error tracking
Errors go to `Console.WriteLine` (`DBConnect.cs:57`) — into the void under IIS — or to the
user's browser.
**Standard:** Serilog → structured logs; health checks; error tracking (Sentry).

### E3 🟠 No tests of any kind
**Standard:** unit tests on the tax engine, numbering and balance logic *at minimum* —
these are the parts where a bug costs money.

### E4 🟠 No migrations; schema drifts by hand
**Standard:** EF Core migrations, versioned and reviewable.

### E5 🟡 Obsolete/conflicting dependencies
`MySql.Data` 6.9.12 (2016-era) *and* `MySqlConnector` both referenced; MySqlConnector marked
`requireReinstallation`. Bootstrap 3 and jQuery 3.4.1 are both EOL/have known CVEs.
**Standard:** one MySQL driver; maintained dependencies; automated vulnerability scanning.

### E6 🟡 Dead code
`WCategory` / `WProducts` (web catalogue, untouched since March 2024, unreferenced) and the
`Home` controller scaffold. Confirm, then delete rather than migrate.

---

## F. Compliance

### F1 🔴 Invoice immutability
`EditItemInvoice` / `EditServiceInvoice` rewrite issued invoices in place. In most VAT
regimes an issued tax invoice **cannot be silently altered** — it is corrected by a credit
note. `DeletedInvoices` suggests soft-delete exists, but edits are not versioned.
**Standard:** issued documents are immutable; corrections via credit note; full audit trail
of who changed what and when.

### F2 🟠 No audit trail
`addedby` / `enteredby` store a *display name string*, not a user id, and nothing records
updates or deletes.
**Standard:** created/modified by user id + UTC timestamp on every row; an audit table for
financial documents.

---

## Priority order

**Before anything else (hours, not weeks):** rotate the DB password (A1) and the SMTP
password (A2); restrict database network access; `git init` (E1).

**Phase 1 (auth + settings):** A3 A4 A5 A6 A7 A9 A10 · A2b A2c *(mail settings + templates + log)* · B7 · E2 E4

**Phase 4 (document engine):** B1 B2 B3 B4 B5 B6 B8 · D1 D2 D3 D4 · F1 F2 · E3

**Ongoing cleanup:** A8 · C1–C6 · D5 · E5 E6

---

## The three that would worry me most

1. **B1 — money as `double`.** Every total, VAT figure and cheque amount in the system is
   computed in binary floating point. This is silently wrong today.
2. **B2/B3 — no transactions, balances mutated in place.** The customer ledger can be
   corrupted by a single failed write, and there is no audit trail to reconstruct it from.
3. **A5 — any logged-in user can grant themselves admin** via `updatepermission`, which
   checks nothing.
