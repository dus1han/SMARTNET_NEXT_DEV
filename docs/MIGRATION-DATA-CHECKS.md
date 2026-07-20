# Migration to live — data checks

Every known defect in the legacy data, and what has to happen to each one before and during the
cutover to live. Verified against `smartnet_invsys_dev` (a copy of live production) on **2026-07-18**.

This is the operational companion to [DATA-AUDIT-FINDINGS.md](DATA-AUDIT-FINDINGS.md), which
explains how each defect was found and what it means. This document is the list to work through.

**Every row below is now detected live in Reports → Data exceptions.** That was not true when the
findings were written: Finding 9 said it surfaced there and did not, and Findings 3 and 4 had no
detection at all. Nothing here relies on re-running an ad-hoc query.

---

## Before cutover — work the list to zero, or decide to accept it

Run **Reports → Data exceptions** with the company filter on **All**. It should show these counts.
If a number is higher on live than shown here, something has been created since 2026-07-18 and needs
looking at before proceeding.

| # | Exception | Count | Value | Blocks cutover? |
|---|---|---|---|---|
| 1 | Duplicate payment | **0** | — | No — remediated, but see below |
| 2 | Paid, no payment | **1** | 21,500 | No — decide, don't block |
| 3 | Lines ≠ header | **4** | 11,125 | No — decide, don't block |
| 4 | Overpaid | **2** | 94,600 | No — decide, don't block |
| 5 | Payment without an invoice | **3** | 70,381 | No — decide, don't block |
| 6 | Supplier paid, not settled | **38** | 1,435,253.01 | No — decide, don't block — *resolved on dev, still to run on live* |
| 7 | Supplier settled twice | **1** | 165,000 | No — decide, don't block |
| 8 | Lines without a document | **105** | 4,678,439.54 | **Yes, if foreign keys are being added** — *resolved on dev, still to run on live* |
| 9 | Duplicate document number | **1** | — | **Yes, for the quotation unique index** |

Total: **155 rows** on live.

> **Dev has diverged from this table, deliberately.** Row 8 was worked to zero on
> `smartnet_invsys_dev` on 2026-07-20, and row 6 with it, so dev now reads **12** and live still reads 155.
> That is the only intended difference. If dev shows anything else changed, something ran that
> should not have.

### Read this before trusting the zero on row 1

The duplicate-payment remediation deleted 49 rows and reports as complete, and the rule now reads 0.
**It did not catch everything.** It matched on same invoice + same amount + *same date*, while the
finding that justified it was written from a wider test — recorded payments against the invoice
total. STI-38 and SNI-915 are listed as examples in Finding 1, are dated weeks apart rather than
same-day, and are **still overpaid today**. They are rows 4 in the table above, not row 1.

So when the remediation is run on live: a clean duplicate-payment count does not mean no invoice is
overpaid. Check the **Overpaid** tile as well. That is the whole reason it exists.

### The two that genuinely block — one is now decided

**Lines without a document (105 groups — 608 invoice lines across 89 invoices, 82 quotation lines
across 16 quotations, 4,678,439.54 of line value between them).** That value is *not* money anybody
is owed: nothing counts these lines and nothing can reach them, because the header they belonged to
is gone. It is recorded only so the scale is clear.

They block because every foreign key the new schema wants will refuse to build while they stand.

**Decided 2026-07-20: delete them.** A line with no document is of no use to anyone. Run
[`infra/sql/remove-orphaned-lines.sql`](../infra/sql/remove-orphaned-lines.sql), which archives the
rows into `archived_orphaned_*` tables *before* deleting them, so the decision stays reversible and
there is a record of what the 4.6M of dangling line value consisted of. It is re-runnable, and it
prints a verification that must read zero on both tables.

**Already run on dev** — 690 rows archived and removed, `invoice_l` 12,598 → 11,990 and
`quotation_l` 6,674 → 6,592, nothing else touched, and the exceptions endpoint now returns
`orphanedLines: 0`. **Still to run on live**, with a backup first: this is a delete, and unlike the
rest of this list it cannot be undone by re-running anything.

Four checks were made before deleting, and are worth repeating on live because any one of them
failing would mean these are *not* safe to remove:

1. **Trimming the header side changes neither count** — so none is a whitespace near-miss that could
   be re-attached instead of deleted.
2. **Every orphan has a NULL `invoice_id` / `quotation_id`.** These tables were adopted and now carry
   a real foreign key beside the legacy varchar, so a line could have had a dead `inno` and a live
   `invoice_id`. None does. *This is the check that matters most.*
3. **None has a header in `del_invoice_h`** — they are not the lines of deleted invoices, which would
   still be worth keeping.
4. **No line has a blank document reference**, so the detection rule covers the whole table.

**Duplicate document number (STQ-0).** Two quotations, two customers, one number. The unique index
is already on `invoice_h`, `cn_h`, `po_h` and `jobs_m`; it cannot go on `quotation_h` until this is
resolved. Deliberately not remediated by us — somebody holds a PDF with STQ-0 printed on it, and
renumbering it to make an index build is exactly the historical rewriting LEGACY-DATA-POLICY
forbids. It needs a business decision, then `quotation_h` joins the unique-index list.

### The seven that need a decision, not a fix

None of these stop the migration. Each is a real-money question for the business, and each is
resolved on the record itself rather than by a script:

- **Overpaid** (STI-38 paid 71,000 twice, seventeen days apart; SNI-915 23,600 twice, three weeks
  apart) — either void the payment that should not be there, or leave the customer in credit, which
  is a true statement of the position. Both are legitimate; the wrong move is to zero a balance.
- **Payment without an invoice** (3 payments: 3,006 naming nothing at all, 1,000 naming STI-30,
  66,375 naming SNI-1045) — money received and attributed to nobody. They cannot be assigned a
  company without guessing which trading entity received it.
- **Paid, no payment** (STI-1068, 21,500) — either the money came in and was never recorded, or the
  balance was zeroed in error. The screen offers both corrections.
- **Lines ≠ header** (STI-1150 is the serious one: the header claims 12,041 against 1,916 of lines)
  — needs a per-invoice decision about which of the two is right.
- **Supplier paid, not settled** (38 invoices, 1,435,253.01) — marked paid with nothing recording
  who was paid or when. All 38 are legacy-origin, so this is not the new app's own data.

  **Decided 2026-07-20: record the missing settlement, dated the invoice date.** Run
  [`infra/sql/settle-supplier-paid-not-settled.sql`](../infra/sql/settle-supplier-paid-not-settled.sql).
  **Already run on dev** (38 rows; `supplier_inv_pay` 1,640 → 1,678); **still to run on live.**

  **The date is an assumption, not a recovered fact.** Nothing records when these were actually
  paid — that *is* the defect. The invoice date makes the record self-consistent and is the one date
  we know relates to the transaction, but it is not evidence payment happened that day. If the real
  dates exist on paper, entering those is strictly better than running the script.

  Every row it writes is therefore marked twice: `referenceno = 'RECONSTRUCTED'`, which shows on the
  supplier invoice screen so a reader can see the settlement was reconstructed rather than recorded at
  the time; and `data_origin = 'reconstructed'`, which is permanent and machine-readable — every
  genuine legacy row has NULL there. That marking is also what makes it reversible:
  `DELETE FROM supplier_inv_pay WHERE data_origin = 'reconstructed';`

  Note this changes no balance. A legacy settlement carries no amount — it stands for the whole
  invoice — so one row records what `paymentstat='Paid'` already asserted.
- **Supplier settled twice** (supplier invoice 621) — a legacy settlement carries no amount and
  stands for the whole invoice, so the second one is a second payment of the same money.

---

## During cutover — re-run, don't assume

The counts above are from a copy taken 2026-07-18. Live has moved since. On cutover day:

1. **Re-run Data exceptions against live before anything else.** Compare to the table above. A
   higher count means new bad data arrived through the legacy app while the rebuild was in progress
   — that is expected, and it is the point of having the screen.
2. **Re-run it after the migration completes.** The counts should be identical. The migration
   neither fixes nor creates these defects; if a number moves, the migration did something it was
   not supposed to.
3. **Check the two blockers specifically**, since they are the only ones that can make a migration
   step fail rather than just report a number.

### Documents — materialise on live, then drop the column

The legacy `docstore` keeps each file as a `LONGBLOB` in the row (Finding C4). Phase 7 slice 4 moved
those bytes onto the filesystem; **on `smartnet_invsys_dev` all 18 are already migrated and verified.**
Live still has to be done, and it is a cutover step because the last part is irreversible.

The order matters, and none of it is automatic:

1. **`mkdir -p /var/www/sys-documents` on the server** before deploying, and confirm the API container
   mounts it. Without the mount the store is inside the container and a redeploy discards every upload —
   silently, because uploading keeps working right up until the redeploy.
2. **Dry run against live**: `dotnet run -- --company <id>`. It writes nothing and lists what it would do.
3. **Decide the company.** `docstore` has no company column and the titles span both entities — there is
   a "VAT CERTIFICATE SMART NET" and a "SMART BRC" in the same table. The tool *requires* `--company`
   rather than inferring one. If the 18 genuinely split across both, run it per company against a
   filtered set, or move the misfiled ones afterwards on the documents screen.
4. **Apply**: `--company <id> --apply`. Re-runnable — `documents.legacy_docstore_id` is uniquely indexed,
   so an already-materialised row is skipped and a concurrent second run is refused by the database.
5. **Verify**: `--verify`. Re-reads every file and re-hashes it against the recorded SHA-256. It exits
   non-zero and says *"NOT SAFE to drop docstore.pdfdoc"* if anything is missing or altered.
6. **Only then drop the column** — `ALTER TABLE docstore DROP COLUMN pdfdoc` — and take a database
   backup first. The `docstore` metadata rows stay (read-only, Legacy Archive); only the in-DB copy of
   the bytes goes.

**Until step 6 the whole thing is reversible**: the BLOBs are still in the database, so deleting the
`documents` rows and their files loses nothing. After step 6 the filesystem is the only copy, which is
why `/var/www/sys-documents` needs to be in the backup set *before* the column goes, not after.

One caveat on the stated benefit: the plan describes this as reclaiming C4 bloat. Measured on the dev
copy, `docstore.pdfdoc` is **9.8 MB across 18 rows** (largest 1.98 MB). The security fix is real — files
leave the web root and sit behind a permission check — but the space is not the reason to do it, and it
is not a reason to hurry step 6.

### The legacy `notes` table keeps its name — the new one is `entity_notes`

Phase 7 slice 5 added per-entity notes. The obvious table name was `notes`, and that is **already a
legacy table**: 49 rows of `id`, `note`, `dt`, scaffolded as `Smartnet.Infrastructure.Entities.Note`
via `.ToTable("notes")`. The first migration failed on `smartnet_invsys_dev` with *"Table 'notes'
already exists"*, which is the only reason it was caught — the safety came from MySQL, not from
anything we had written.

The new table is therefore **`entity_notes`** and the entity is **`EntityNote`** (the same
disambiguation `StoredDocument` made when "document" was taken). Nothing dual-writes the legacy
`notes`: it carries no entity reference and no author, so there is nothing in it to keep in step, and
per LEGACY-DATA-POLICY it stays where it is and stays readable through the Legacy Archive.

**At cutover:** nothing to do beyond letting the migration run — it creates `entity_notes` and leaves
`notes` untouched. Confirm both exist afterwards and that `notes` still reads 49 rows (or whatever
live holds); a migration that touched it did something it was not supposed to. **Do not fold the 49
legacy rows into `entity_notes`** — they cannot be attributed to a record or an author, which is the
whole reason the legacy table was not ported.

A regression test now pins this: `LegacyTableNameTests` asserts the exact set of table names the new
context shares with the legacy schema. Adoptions are listed explicitly, so any *new* table that
quietly takes a legacy name fails the build instead of the migration.

### Four dates have a five-digit year

Found while moving the lists onto server-side paging, which orders on the stored `varchar` date. All
four are legacy typos from an app that never validated the field:

| Record | Table | Stored date | Almost certainly |
|---|---|---|---|
| SNQ-752 | `quotation_h` | `32024-11-11` | 2024-11-11 |
| SNQ-927 | `quotation_h` | `42025-05-24` | 2025-05-24 |
| SNQ-953 | `quotation_h` | `20225-05-16` | 2025-05-16 |
| payment id 799 (supplier invoice 862, ref CASH) | `supplier_inv_pay` | `22025-02-11` | 2025-02-11 |

Every other date in `invoice_h`, `quotation_h`, `supplier_invoice`, `payments` and `supplier_inv_pay`
is ISO `yyyy-MM-dd`, which is what makes ordering on the text chronological and the paging sound.

**They moved.** The old screens parsed the date in C#, failed, and fell back to `DateOnly.MinValue`, so
these sorted to the *bottom* of their lists. Ordered as text, `2 2…`, `3…` and `4…` sort above `2 0…`,
so they now appear at the *top* as though they were the newest. Nothing is lost or duplicated — paging
was verified complete over every row of all five lists — but four records are in the wrong place until
the dates are corrected.

Each is a one-line update and needs no migration; they are listed here because they are decisions about
someone's documents, not something to fix silently.

### Not on the exceptions screen, but check anyway

Three things that are structural rather than per-record, so they have no row to show:

- **The receivables ledger is derived, never stored.** It was rebuilt from the documents, and there
  are deliberately **no opening-balance rows** in any section. If any appear, something reintroduced
  them. Reconciliation showed 2,482 of 2,485 invoices unchanged by the rebuild; the 3 that moved
  account for −73,100 and are the overpayment/orphan cases above.
- **The payables ledger holds no legacy rows at all** — a legacy supplier invoice's outstanding is
  read from `paymentstat`. This is why the supplier rules check for the presence or absence of a
  settlement row and never for a mismatched figure.
- **Money is `varchar(100)` throughout the legacy tables** (Finding 5) and there are three primary
  keys and no foreign keys in 49 tables (Finding 6). Everything that reads legacy data parses
  defensively as a result. This does not change at cutover.

---

## Resolved before this point, for the record

- **Finding 1 — 1,575,374 of duplicate payments** (44 groups, 49 extra rows). Remediated; the rule
  stays live so a new duplicate surfaces. Now reads 0.
- **Finding 7 — two users with 4-character plaintext passwords.** Gone with the rebuild's identity
  system.
- **Finding 11 — the invoice prefix is not a constant.** Company 2 moved from `SNI-` to
  `26JUL_SNIN_` mid-sequence with the counter running straight through. Handled by design: the
  prefix is stored as a template rendered at allocation time, so August produces `26AUG_SNIN_`
  without an edit.
