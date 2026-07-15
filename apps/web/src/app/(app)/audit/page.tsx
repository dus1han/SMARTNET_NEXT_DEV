"use client";

/**
 * The admin audit log viewer.
 *
 * The History tab answers "what happened to *this* record?"; this screen answers the questions that
 * span records — "who exported the customer list?", "every failed login this week", "what did this
 * user change?". Phase 1 wrote `audit_log` on every save, login, print, email and export and then
 * gave nobody a way to read across it. This is that way.
 *
 * It reads the new `audit_log` (not the legacy tables), scoped server-side to the companies the token
 * grants, and guarded by `audit.view` — which the two administrator roles hold and a business role
 * does not. The filters ride the request, so there is no stale-filter bug of the kind the legacy
 * reports have. The result is capped and carries its true total: on the one screen whose whole job is
 * to be believed, a truncated list must never look complete.
 */

import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { ApiError } from "@/lib/api";
import { AUDIT_ACTIONS, getAuditFacets, getAuditLog, type AuditEntry } from "@/lib/audit";
import { parseChanges } from "@/lib/history";
import { PageHeader } from "@/components/shell/app-shell";
import { DataTable, type ColumnDef } from "@/components/data-table";
import { Badge, Card, ErrorBanner, FadeIn, Input, Select } from "@/components/ui";

export default function AuditLogPage() {
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const [action, setAction] = useState("");
  const [entityType, setEntityType] = useState("");
  const [user, setUser] = useState("");

  const facets = useQuery({ queryKey: ["audit-facets"], queryFn: getAuditFacets });

  const log = useQuery({
    queryKey: ["audit-log", from, to, action, entityType, user],
    queryFn: () =>
      getAuditLog({
        from: from || undefined,
        to: to || undefined,
        action: action || undefined,
        entityType: entityType || undefined,
        user: user ? Number(user) : undefined,
      }),
  });

  const loadError = log.error as ApiError | null;
  const data = log.data;
  const capped = data !== undefined && data.total > data.entries.length;

  return (
    <FadeIn className="space-y-6">
      <PageHeader
        title="Audit log"
        description="Every change, login, export and print recorded across the system — who did what, when, and from where."
      />

      <Card className="flex flex-wrap items-end gap-4 p-4">
        <Select label="Action" value={action} onChange={(e) => setAction(e.target.value)} className="w-40">
          <option value="">All actions</option>
          {AUDIT_ACTIONS.map((a) => (
            <option key={a} value={a}>
              {a}
            </option>
          ))}
        </Select>

        <Select
          label="Record type"
          value={entityType}
          onChange={(e) => setEntityType(e.target.value)}
          className="w-44"
        >
          <option value="">All records</option>
          {facets.data?.entityTypes.map((type) => (
            <option key={type} value={type}>
              {type}
            </option>
          ))}
        </Select>

        <Select label="User" value={user} onChange={(e) => setUser(e.target.value)} className="w-48">
          <option value="">All users</option>
          {facets.data?.actors.map((actor) => (
            <option key={actor.id} value={actor.id}>
              {actor.name}
            </option>
          ))}
        </Select>

        <Input
          label="From"
          type="date"
          value={from}
          max={to || undefined}
          onChange={(e) => setFrom(e.target.value)}
          className="w-40"
        />
        <Input
          label="To"
          type="date"
          value={to}
          min={from || undefined}
          onChange={(e) => setTo(e.target.value)}
          className="w-40"
        />
      </Card>

      {loadError && <ErrorBanner message={loadError.message} correlationId={loadError.correlationId} />}

      <DataTable
        columns={columns}
        rows={data?.entries}
        loading={log.isPending}
        searchable={searchText}
        searchPlaceholder="Search the visible events…"
        defaultSort={{ id: "when", desc: true }}
        pageSize={25}
        empty={{
          title: "No matching events",
          description: "Nothing in the log matches these filters. Widen the date range or clear a filter.",
        }}
      />

      {capped && (
        // A list that silently stops at its limit reads as a complete history. On this screen, of all
        // screens, that is the one thing it must not do.
        <p className="text-center text-xs text-muted">
          Showing the {data!.entries.length} most recent of {data!.total} matching events. Narrow the
          filters to see the rest.
        </p>
      )}
    </FadeIn>
  );
}

const columns: ColumnDef<AuditEntry, unknown>[] = [
  {
    id: "when",
    accessorFn: (row) => row.changedAt,
    header: "When",
    cell: ({ row }) => <Timestamp at={row.original.changedAt} />,
  },
  {
    id: "action",
    accessorFn: (row) => row.action,
    header: "Action",
    cell: ({ row }) => <Badge tone={tone(row.original.action)}>{row.original.action}</Badge>,
  },
  {
    id: "user",
    accessorFn: (row) => actor(row),
    header: "User",
    cell: ({ row }) => <span className="whitespace-nowrap text-text">{actor(row.original)}</span>,
  },
  {
    id: "record",
    accessorFn: (row) => `${row.entityType} ${row.entityId}`,
    header: "Record",
    cell: ({ row }) => (
      <span className="whitespace-nowrap">
        <span className="text-text">{row.original.entityType}</span>
        {row.original.entityId && row.original.entityId !== "*" && (
          <span className="text-muted"> · {row.original.entityId}</span>
        )}
      </span>
    ),
  },
  {
    id: "detail",
    accessorFn: (row) => summarise(row),
    header: "Detail",
    enableSorting: false,
    cell: ({ row }) => <Detail entry={row.original} />,
  },
  // No IP column: behind nginx the request's RemoteIpAddress is the proxy's, not the client's (there
  // is no ForwardedHeaders handling), and on a single dev machine it is always ::1 (loopback). The
  // ip_address is still captured and stored on every row — the day forwarded headers are trusted and
  // it reflects the real client, this column comes back.
];

/** What the whole row searches on — what the reader can SEE, not the raw ids and timestamps. */
function searchText(row: AuditEntry): string {
  return [row.action, actor(row), row.entityType, row.entityId, row.reason, summarise(row)]
    .filter(Boolean)
    .join(" ");
}

/**
 * The actor. Null when the account has since been removed — which is exactly why the log stores the
 * id and resolves the name at read time, rather than copying a name in at write time as the legacy
 * app did. A failed login has no authenticated user, so it is anonymous.
 */
function actor(entry: AuditEntry): string {
  return entry.changedByName ?? (entry.changedBy === null ? "Anonymous" : `User ${entry.changedBy}`);
}

function Detail({ entry }: { entry: AuditEntry }) {
  const text = summarise(entry);

  if (!text) {
    return <span className="text-muted">—</span>;
  }

  return (
    // The raw diff is one hover away, for the rare moment the summary is not enough.
    <span className="text-sm text-text" title={entry.changes ?? undefined}>
      {text}
    </span>
  );
}

/**
 * A one-line reading of an event.
 *
 * A stated reason wins — it is the human explanation. Otherwise the stored `changes` is either a
 * field diff (a mutation) or a details object (a login, an export), and the two read differently: the
 * first as the fields that moved, the second as its own key/values.
 */
function summarise(entry: AuditEntry): string {
  if (entry.reason) {
    return entry.reason;
  }

  const changes = parseChanges(entry.changes) as Record<string, unknown>;
  const keys = Object.keys(changes);

  if (keys.length === 0) {
    return "";
  }

  const isFieldDiff = keys.every((key) => {
    const value = changes[key];
    return value !== null && typeof value === "object" && ("from" in value || "to" in value);
  });

  if (isFieldDiff) {
    return keys.length === 1 ? `${keys[0]} changed` : `${keys.length} fields changed`;
  }

  return keys.map((key) => `${key}: ${String(changes[key])}`).join(" · ");
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

/**
 * The stored instant in the reader's own time zone. The API stores and returns UTC; the exact UTC
 * value is one hover away, and the ISO string sorts correctly as the column's accessor.
 */
function Timestamp({ at }: { at: string }) {
  const instant = new Date(at.endsWith("Z") ? at : `${at}Z`);

  return (
    <time
      dateTime={instant.toISOString()}
      title={instant.toUTCString()}
      className="whitespace-nowrap text-sm text-text"
    >
      {instant.toLocaleString()}
    </time>
  );
}
