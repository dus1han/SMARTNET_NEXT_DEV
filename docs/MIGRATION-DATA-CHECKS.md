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
| 6 | Supplier paid, not settled | **38** | 1,435,253.01 | No — decide, don't block |
| 7 | Supplier settled twice | **1** | 165,000 | No — decide, don't block |
| 8 | Lines without a document | **105** | 4,678,439.54 | **Yes, if foreign keys are being added** |
| 9 | Duplicate document number | **1** | — | **Yes, for the quotation unique index** |

Total: **155 rows**.

### Read this before trusting the zero on row 1

The duplicate-payment remediation deleted 49 rows and reports as complete, and the rule now reads 0.
**It did not catch everything.** It matched on same invoice + same amount + *same date*, while the
finding that justified it was written from a wider test — recorded payments against the invoice
total. STI-38 and SNI-915 are listed as examples in Finding 1, are dated weeks apart rather than
same-day, and are **still overpaid today**. They are rows 4 in the table above, not row 1.

So when the remediation is run on live: a clean duplicate-payment count does not mean no invoice is
overpaid. Check the **Overpaid** tile as well. That is the whole reason it exists.

### The two that genuinely block

**Lines without a document (105 groups — 608 invoice lines across 89 invoices, 82 quotation lines
across 16 quotations, 4,678,439.54 of line value between them).** That value is *not* money anybody
is owed: nothing counts these lines and nothing can reach them, because the header they belonged to
is gone. It is recorded only so the scale is clear before somebody decides to delete them.

They block because every foreign key the new schema wants will refuse to build while they stand.
Either delete them as part of the cutover, or leave the foreign keys off — but that is a decision to
take deliberately, not to discover halfway through a migration script.

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
