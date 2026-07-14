# Versioning & Audit — cross-cutting requirement

**Requirement:** every entity in the system is versioned and audited — *who, when, why*.

This is infrastructure, not a feature of each module. It is built once, in Phase 1, and
enforced at the persistence layer so **no developer can bypass it** — including a future
developer, in a hurry, at 6pm.

---

## 1. What "audited" means here

Three layers, each answering a different question:

| Layer | Question it answers | Mechanism |
|---|---|---|
| **Audit columns** | Who touched this row last? | Columns on every table |
| **Audit log** | What exactly changed, from what to what, and why? | Append-only `audit_log`, written automatically |
| **Version snapshots** | What did this document look like on 3 March? Print it. | `document_versions` — full point-in-time snapshot |

Master data (customers, items, suppliers) needs layers 1 and 2.
Financial documents (invoice, quotation, credit note, PO, supplier invoice, job card,
payment, cheque, expense) need all three — because you must be able to **reproduce the
document as it was issued**, not merely list what changed.

---

## 2. Audit columns — on every table

```
created_by      BIGINT      -- user id, never a display name
created_at      DATETIME    -- UTC
updated_by      BIGINT
updated_at      DATETIME    -- UTC
deleted_by      BIGINT NULL -- soft delete
deleted_at      DATETIME NULL
row_version     INT         -- optimistic concurrency
```

Two deliberate choices:

- **User id, not a name.** The legacy app stores `addedby` as a display string
  (`"Saboor A. : 2026-07-14 10:33:12"`). Rename the user and history becomes ambiguous.
- **`row_version`** gives optimistic concurrency for free: if two users edit the same
  invoice, the second save fails loudly instead of silently overwriting the first.
  (The legacy app has no protection against this at all.)

**Nothing is hard-deleted.** Deletes are soft, so `delinvoice`, `delCN`, `deletepay`,
`deleteExpense`, `delItemStock` etc. all become recoverable and attributable.

---

## 3. `audit_log` — append-only, written automatically

```sql
CREATE TABLE audit_log (
  id             BIGINT PRIMARY KEY AUTO_INCREMENT,
  company_id     BIGINT,
  entity_type    VARCHAR(64),    -- 'Invoice', 'Customer', 'UserPermission', ...
  entity_id      VARCHAR(64),
  action         ENUM('Create','Update','Delete','Restore','Login','Print','Email','Export'),
  changed_by     BIGINT,
  changed_at     DATETIME,       -- UTC
  reason         VARCHAR(500),   -- the "why"
  changes        JSON,           -- { "field": { "from": x, "to": y }, ... } — only changed fields
  ip_address     VARCHAR(45),
  user_agent     VARCHAR(255),
  correlation_id CHAR(36),       -- ties the row to a request and to the logs
  INDEX (entity_type, entity_id, changed_at),
  INDEX (changed_by, changed_at),
  INDEX (company_id, changed_at)
);
```

### It writes itself
An **EF Core `SaveChangesInterceptor`** inspects the change tracker on every save, diffs
each modified entity, and writes the `audit_log` rows inside **the same transaction** as the
business change. Consequences:

- A developer cannot forget to audit — there is no code to remember to write.
- Audit and data cannot diverge: either both commit or neither does.
- Adding a new entity later gets audit for free.

### It cannot be edited
The application's database user is granted `INSERT` and `SELECT` on `audit_log` — **not
`UPDATE` or `DELETE`**. An append-only log that the app can rewrite is not evidence.
Enforced by a GRANT, not by convention.

### Sensitive fields are redacted, not logged
`password_hash`, SMTP `password_encrypted` and similar are recorded as *changed*, never with
their values. Redaction list is explicit and reviewed.

### Non-mutation events are audited too
Auditing only writes misses the questions people actually ask. `Login` (success **and
failure**), `Print`, `Email`, `Export` are logged — so "who exported the customer list?" and
"was this invoice ever emailed?" become answerable. Today neither is.

---

## 4. `document_versions` — full snapshots

```sql
CREATE TABLE document_versions (
  id             BIGINT PRIMARY KEY AUTO_INCREMENT,
  company_id     BIGINT,
  doc_type       VARCHAR(32),   -- INVOICE | QUOTATION | CN | PO | SUPINV | JOBCARD
  doc_id         BIGINT,
  version_no     INT,
  snapshot       JSON,          -- complete header + lines + resolved tax, as saved
  changed_by     BIGINT,
  changed_at     DATETIME,
  reason         VARCHAR(500),
  UNIQUE (doc_type, doc_id, version_no)
);
```

Version 1 is written at creation, not just on edit — otherwise the original is the one
version you cannot recover.

The snapshot is **self-contained**: it carries the resolved tax rates, the company header
values and the line data as they stood. So reprinting version 2 of an invoice from last year
reproduces *that* document — not today's VAT rate applied to last year's lines. This is the
whole point, and it's exactly what the legacy system gets wrong.

**UI:** every document gets a *History* tab — version list, side-by-side diff, "print this
version", and (permission-gated) restore.

---

## 5. The "why" — capturing reason

`reason` is only worth having if it's actually filled in. Proposed policy, itself a setting:

| Action | Reason |
|---|---|
| Edit an **issued** invoice / credit note | **Mandatory** — free text, min 10 chars |
| Delete anything | **Mandatory** |
| Change a user's permissions, reset a password | **Mandatory** |
| Change tax rates, numbering, company details | **Mandatory** |
| Edit a draft document | Optional |
| Create anything | Not prompted (the record *is* the reason) |
| Edit master data (customer, item, supplier) | Optional |

Mechanism: the client sends `X-Change-Reason` on mutating requests; a middleware puts it in
an ambient context the interceptor reads. Endpoints that require a reason reject the request
without one — so the rule is enforced server-side, not by a hopeful frontend.

⚠️ **Set this policy deliberately.** Demand a reason for every keystroke and staff will type
`"."` forever, which is worse than no reason at all — it looks like an audit trail and isn't.

---

## 6. Retention & volume

- `audit_log` grows fastest. Partition by month; archive to cold storage after **24 months**;
  never delete financial-document audit rows.
- `document_versions` is bounded by edit frequency and is small by comparison.
- JSON diffs store **only changed fields**, not whole rows.
- Reporting/export actions are logged at summary level (what, by whom), not row-by-row.

---

## 7. Where this lands in the plan

**Phase 1** (with auth, before any business module):
- audit columns + `row_version` on every entity
- `audit_log` + the SaveChanges interceptor + the append-only GRANT
- `document_versions` table and the snapshot service
- reason-capture middleware and policy settings
- redaction list

**Phase 2:** the reusable *History* tab component (version list, diff view, restore) that
every later module drops in.

**Phase 4:** an admin **Audit Log viewer** — filter by user, entity, action, date range;
export. Without a viewer, an audit trail is a table nobody reads.

**Every later phase** inherits all of the above for free. That is the point of doing it first.

**Added effort:** ~1 week in Phase 1, ~0.5 week in Phase 2, ~0.5 week in Phase 4.
Total ~2 weeks — against retrofitting it later, which would touch every module.

---

## 8. What this closes

- **ISSUES F2** — no audit trail (`addedby` is a display-name string; updates and deletes
  record nothing at all).
- **ISSUES F1** — invoice immutability. You chose to keep invoices editable; versioning is
  what makes that defensible to an auditor.
- **ISSUES B3** — balances mutated in place with no way to reconstruct the correct figure.
- **Concurrency** — two users editing the same record now conflict loudly instead of one
  silently overwriting the other.
