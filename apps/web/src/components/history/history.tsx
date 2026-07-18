"use client";

import { useQuery } from "@tanstack/react-query";
import { History as HistoryIcon, Printer, RotateCcw } from "lucide-react";
import { useState } from "react";
import { ApiError } from "@/lib/api";
import { me } from "@/lib/auth";
import { cn } from "@/lib/cn";
import {
  documentVersion,
  documentVersions,
  parseChanges,
  recordHistory,
  type AuditEntry,
  type DocumentVersionSummary,
} from "@/lib/history";
import { Badge, Button, ErrorBanner, Skeleton, toast } from "@/components/ui";
import { useReason } from "@/components/form";
import { changeRows, factRows, fieldLabel, formatValue, isRedacted, snapshotRows, type DiffRow, type Fact } from "./diff";
import { printVersion } from "./print";

/**
 * THE HISTORY TAB.
 *
 * Dropped into every document module from Phase 5 onward, and into any record worth asking "who
 * changed this?" about. Phase 1 wrote `audit_log` and `document_versions` on every single save and
 * then gave nobody a way to read either — a trail nobody can see is a trail nobody trusts.
 *
 * It renders two things that are deliberately not the same:
 *
 *   1. **The audit log** — what *changed*, field by field, with who and why. Every auditable record
 *      has this, today.
 *   2. **Document versions** — what the document *looked like* at a point in time, so it can be
 *      diffed against its neighbour and reprinted as it stood. Only documents have these, and only
 *      from Phase 5, when something starts writing them.
 *
 * So `document` is optional, and until Phase 5 the tab is the audit log — which is exactly what the
 * Phase 2 exit criterion says it should be.
 */
export interface HistoryProps {
  /** The CLR entity name, as stored in `audit_log.entity_type`: "User", "Invoice", "TaxRate". */
  entityType: string;
  entityId: string | number;

  /** Documents only. Enables the version list, the snapshot diff, the reprint and the restore. */
  document?: DocumentTarget;

  className?: string;
}

export interface DocumentTarget {
  /** INVOICE | QUOTATION | CN | PO | SUPINV | JOBCARD | PAYMENT | CHEQUE | EXPENSE. */
  docType: string;
  docId: number;

  /** What the printed page is headed with — "Invoice SN-INV-00042". */
  title: string;

  /**
   * Restoring a version rewrites a live document, so the module that owns the document owns the
   * write — this component owns the prompt, and asks why first.
   *
   * Passing it is the permission gate: the host has the user's claims and knows whether they may
   * edit *this* document type. A restore the user cannot perform is not a button they should see —
   * and, as everywhere else, hiding it is a courtesy: the endpoint behind it re-checks (ISSUES A5).
   */
  onRestore?: (versionNo: number, reason: string) => Promise<void>;
}

/** The permission that guards every history endpoint. Business roles do not hold it. */
const AUDIT_VIEW = "audit.view";

export function History({ entityType, entityId, document, className }: HistoryProps) {
  const reason = useReason();

  const [selected, setSelected] = useState<number | null>(null);

  // Cached by the shell, so this costs nothing. Checking it saves the user a 403 they can do
  // nothing about; it does not *authorise* anything — the server does that, on every call.
  const session = useQuery({ queryKey: ["me"], queryFn: me });
  const permitted = session.data?.permissions.includes(AUDIT_VIEW) ?? false;

  const events = useQuery({
    queryKey: ["history", entityType, String(entityId)],
    queryFn: () => recordHistory(entityType, entityId),
    enabled: permitted,
  });

  const versions = useQuery({
    queryKey: ["history", "versions", document?.docType, document?.docId],
    queryFn: () => documentVersions(document!.docType, document!.docId),
    enabled: permitted && document !== undefined,
  });

  if (session.isPending) {
    return <Loading />;
  }

  if (!permitted) {
    return (
      <p className={cn("py-8 text-center text-sm text-muted", className)}>
        You do not have permission to see this record&rsquo;s history.
      </p>
    );
  }

  const error = (events.error ?? versions.error) as ApiError | null;

  return (
    <div className={cn("space-y-5", className)}>
      {error && <ErrorBanner message={error.message} correlationId={error.correlationId} />}

      {document && (
        <Versions
          document={document}
          versions={versions.data}
          loading={versions.isPending}
          selected={selected}
          onSelect={(versionNo) => setSelected((current) => (current === versionNo ? null : versionNo))}
          ask={reason.ask}
        />
      )}

      {selected !== null && document && (
        <SnapshotDiff document={document} versionNo={selected} />
      )}

      <Timeline events={events.data?.entries} total={events.data?.total} loading={events.isPending} />

      {reason.dialog}
    </div>
  );
}

// --- The audit log -------------------------------------------------------------------------

function Timeline({ events, total, loading }: {
  events: AuditEntry[] | undefined;
  total: number | undefined;
  loading: boolean;
}) {
  if (loading) return <Loading />;

  if (!events?.length) {
    return (
      <div className="py-10 text-center">
        <HistoryIcon className="mx-auto size-6 text-muted" aria-hidden />
        <p className="mt-2 text-sm font-medium text-text">No changes recorded</p>
        <p className="mt-1 text-sm text-muted">
          Nothing has happened to this record since auditing began.
        </p>
      </div>
    );
  }

  return (
    <div>
      <ol className="space-y-3">
        {events.map((event) => (
          <li key={event.id}>
            <Event event={event} />
          </li>
        ))}
      </ol>

      {total !== undefined && total > events.length && (
        // A list that silently stops at its limit reads as a complete history. On this screen, of
        // all screens, that is the one thing it must not do.
        <p className="mt-4 text-center text-xs text-muted">
          Showing the {events.length} most recent of {total} changes.
        </p>
      )}
    </div>
  );
}

/**
 * Mutations carry a field-level diff. Everything else — a print, an email, an export, a login — is an
 * event that happened once, with no "before". Rendering those through the diff table produced a
 * before/after grid with both columns empty, which reads as "something changed and we lost it".
 */
const MUTATIONS = new Set(["Create", "Update", "Delete", "Restore"]);

function Event({ event }: { event: AuditEntry }) {
  const isMutation = MUTATIONS.has(event.action);
  const rows = isMutation ? changeRows(parseChanges(event.changes)) : [];
  const facts = isMutation ? [] : factRows(event.changes);

  return (
    <article className="rounded-lg border border-subtle bg-surface p-4">
      <header className="flex flex-wrap items-center gap-x-3 gap-y-1">
        <Badge tone={tone(event.action)}>{event.action}</Badge>

        <span className="text-sm font-medium text-text">
          {/* Null when the actor no longer exists — which is why the log stores the id, not a name
              copied in at write time as the legacy app did. */}
          {event.changedByName ?? (event.changedBy === null ? "Anonymous" : `User ${event.changedBy}`)}
        </span>

        <Timestamp at={event.changedAt} />

        {event.ipAddress && (
          <span className="font-mono text-xs text-muted">{event.ipAddress}</span>
        )}
      </header>

      {event.reason && (
        <p className="mt-2 border-l-2 border-subtle pl-3 text-sm text-muted">
          &ldquo;{event.reason}&rdquo;
        </p>
      )}

      {rows.length > 0 && <Diff rows={rows} />}
      {facts.length > 0 && <Facts facts={facts} />}
    </article>
  );
}

/**
 * What an event was about, stated plainly: "Sent to nimal@…, thilanga@…", "Document Job sheet".
 *
 * A list, not a two-column diff — these have one value each, and the second column would be empty
 * by definition.
 */
function Facts({ facts }: { facts: Fact[] }) {
  return (
    <dl className="mt-3 space-y-1 text-sm">
      {facts.map((fact) => (
        <div key={fact.label} className="flex flex-wrap gap-x-2">
          <dt className="text-muted">{fact.label}</dt>
          <dd className="min-w-0 wrap-break-word text-text">{fact.value}</dd>
        </div>
      ))}
    </dl>
  );
}

// --- Document versions ---------------------------------------------------------------------

function Versions({ document, versions, loading, selected, onSelect, ask }: {
  document: DocumentTarget;
  versions: DocumentVersionSummary[] | undefined;
  loading: boolean;
  selected: number | null;
  onSelect: (versionNo: number) => void;
  ask: ReturnType<typeof useReason>["ask"];
}) {
  if (loading) return <Skeleton className="h-24" />;

  if (!versions?.length) {
    return (
      <p className="rounded-lg border border-dashed border-subtle px-4 py-3 text-sm text-muted">
        No saved versions of this document yet.
      </p>
    );
  }

  return (
    <ol className="divide-y divide-subtle overflow-hidden rounded-lg border border-subtle">
      {versions.map((version) => (
        <li
          key={version.id}
          className={cn(
            "flex flex-wrap items-center gap-x-3 gap-y-2 px-4 py-3 transition-colors duration-150",
            selected === version.versionNo ? "bg-primary-ghost" : "bg-surface",
          )}
        >
          <button
            type="button"
            onClick={() => onSelect(version.versionNo)}
            className="flex min-w-0 flex-1 items-center gap-3 text-left"
            aria-expanded={selected === version.versionNo}
          >
            <Badge tone={version.versionNo === versions[0].versionNo ? "success" : "neutral"}>
              v{version.versionNo}
            </Badge>

            <span className="min-w-0">
              <span className="block truncate text-sm text-text">
                {version.changedByName ?? "Unknown"}
                {version.reason && <span className="text-muted"> — {version.reason}</span>}
              </span>
              <Timestamp at={version.changedAt} className="block" />
            </span>
          </button>

          <Button variant="ghost" size="sm" onClick={() => reprint(document, version)}>
            <Printer />
            Print
          </Button>

          {document.onRestore && (
            <Button
              variant="ghost"
              size="sm"
              onClick={() =>
                // Restoring overwrites the live document with an older one. AUDIT.md §5 makes that
                // one of the changes that must explain itself, and the server rejects it without
                // the header regardless of what this dialog does.
                ask({
                  title: `Restore version ${version.versionNo}`,
                  description:
                    "The document is rewritten to look as it did in this version. The current "
                    + "version is kept — nothing is lost, and the restore is itself a new version.",
                  confirmLabel: "Restore this version",
                  onConfirm: async (why) => {
                    await document.onRestore!(version.versionNo, why);
                    toast.success(`Restored version ${version.versionNo}.`);
                  },
                })
              }
            >
              <RotateCcw />
              Restore
            </Button>
          )}
        </li>
      ))}
    </ol>
  );
}

/** The diff between the selected version and the one before it — the question "what did they change?" */
function SnapshotDiff({ document, versionNo }: { document: DocumentTarget; versionNo: number }) {
  const current = useQuery({
    queryKey: ["history", "version", document.docType, document.docId, versionNo],
    queryFn: () => documentVersion(document.docType, document.docId, versionNo),
  });

  const previous = useQuery({
    queryKey: ["history", "version", document.docType, document.docId, versionNo - 1],
    queryFn: () => documentVersion(document.docType, document.docId, versionNo - 1),
    // Version 1 is the document as first created. There is nothing before it, and asking for
    // version 0 is a 404 the user did not cause.
    enabled: versionNo > 1,
  });

  if (current.isPending || (versionNo > 1 && previous.isPending)) {
    return <Skeleton className="h-32" />;
  }

  if (!current.data) return null;

  const rows = snapshotRows(previous.data?.snapshot ?? null, current.data.snapshot);

  return (
    <section className="rounded-lg border border-subtle bg-surface p-4">
      <h3 className="text-sm font-medium text-text">
        {versionNo > 1
          ? `What changed between version ${versionNo - 1} and version ${versionNo}`
          : "Version 1 — the document as first created"}
      </h3>

      {rows.length === 0 ? (
        <p className="mt-2 text-sm text-muted">Nothing in the document itself changed.</p>
      ) : (
        <Diff rows={rows} />
      )}
    </section>
  );
}

function reprint(document: DocumentTarget, version: DocumentVersionSummary) {
  documentVersion(document.docType, document.docId, version.versionNo)
    .then((detail) => printVersion(detail, document.title))
    .catch((error: unknown) =>
      toast.error(error instanceof Error ? error.message : "That version could not be printed."),
    );
}

// --- Shared --------------------------------------------------------------------------------

/**
 * Before and after, side by side.
 *
 * Two columns rather than a red/green inline diff: the fields here are values, not prose, and
 * "1,250.00 → 1,520.00" is a transposition somebody has to be able to *see*.
 */
function Diff({ rows }: { rows: DiffRow[] }) {
  return (
    <div className="mt-3 overflow-x-auto">
      <table className="w-full min-w-md text-sm">
        <thead>
          <tr className="text-left text-xs uppercase tracking-wide text-muted">
            <th className="pb-1 pr-3 font-medium">Field</th>
            <th className="pb-1 pr-3 font-medium">Before</th>
            <th className="pb-1 font-medium">After</th>
          </tr>
        </thead>

        <tbody className="divide-y divide-subtle">
          {rows.map((row) => (
            <tr key={row.field} className="align-top">
              <td className="py-1.5 pr-3 text-muted">{fieldLabel(row.field)}</td>

              <td
                className={cn(
                  "py-1.5 pr-3 break-words text-muted",
                  isRedacted(row) && "italic",
                )}
              >
                {formatValue(row.before)}
              </td>

              <td
                className={cn(
                  "py-1.5 break-words font-medium text-text",
                  isRedacted(row) && "font-normal italic text-muted",
                )}
              >
                {formatValue(row.after)}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

/**
 * The stored instant, in the reader's own time zone.
 *
 * The API stores and returns UTC — the legacy app wrote server-local time into the database, which
 * is why its timestamps cannot be compared across a clock change. Rendering is the only place a
 * local time is correct, and the exact UTC value is one hover away.
 */
function Timestamp({ at, className }: { at: string; className?: string }) {
  const instant = new Date(at.endsWith("Z") ? at : `${at}Z`);

  return (
    <time
      dateTime={instant.toISOString()}
      title={instant.toUTCString()}
      className={cn("text-xs text-muted", className)}
    >
      {instant.toLocaleString()}
    </time>
  );
}

function Loading() {
  return (
    <div className="space-y-3">
      <Skeleton className="h-20" />
      <Skeleton className="h-20" />
      <Skeleton className="h-20" />
    </div>
  );
}

function tone(action: string): "neutral" | "success" | "warning" | "danger" {
  switch (action) {
    case "Create":
      return "success";
    case "Delete":
      return "danger";
    case "Update":
    case "Restore":
      return "warning";
    default:
      // Login, Print, Email, Export — events, not changes.
      return "neutral";
  }
}
