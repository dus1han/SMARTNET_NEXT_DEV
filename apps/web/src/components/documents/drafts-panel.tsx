"use client";

/**
 * The Drafts view behind a list screen's Drafts filter.
 *
 * One component for all four document types, because a draft is the same thing in each: unraised work,
 * whose row says who left it and when, and whose only two actions are to pick it up or throw it away.
 * The columns differ from the document list's on purpose — a draft has no number, no date that means
 * anything yet, and no status beyond "not raised", so showing those columns empty would be four columns
 * of dashes pretending to be information.
 */

import { useState, type ReactNode } from "react";
import { useRouter } from "next/navigation";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { FilePenLine, Trash2 } from "lucide-react";
import { ApiError } from "@/lib/api";
import { cn } from "@/lib/cn";
import { deleteDraft, listDrafts, type DraftDocType, type DraftSummary } from "@/lib/drafts";
import { instantFromApi } from "@/lib/time";
import { DataTable, type ColumnDef } from "@/components/data-table";
import { formatMoney } from "@/components/reports";
import { Button, Dialog, ErrorBanner, toast } from "@/components/ui";

/** Which half of a list screen is showing: the documents it has raised, or the ones it has not. */
export type DocumentView = "issued" | "drafts";

/**
 * The switch between a list's raised documents and its drafts.
 *
 * Two views rather than a status column in one list, because they are not two states of one thing: a
 * raised document has a number, a date and a place in the ledger, and a draft has none of those. Mixing
 * them would put half a dozen empty columns beside every draft row and invite somebody to sort a list by
 * a number that half of it does not have.
 */
export function DocumentViewFilter({
  view,
  onChange,
  docType,
  issuedLabel,
}: {
  view: DocumentView;
  onChange: (view: DocumentView) => void;
  docType: DraftDocType;
  issuedLabel: string;
}) {
  const count = useDraftCount(docType);

  return (
    <div className="flex items-center gap-1 rounded-lg border border-subtle bg-surface-sunken p-1">
      <Tab active={view === "issued"} onClick={() => onChange("issued")}>
        {issuedLabel}
      </Tab>
      <Tab active={view === "drafts"} onClick={() => onChange("drafts")}>
        Drafts
        {count > 0 && (
          <span
            className={cn(
              "ml-1.5 rounded-full px-1.5 py-0.5 text-xs tabular",
              view === "drafts" ? "bg-primary/15 text-primary" : "bg-surface text-muted",
            )}
          >
            {count}
          </span>
        )}
      </Tab>
    </div>
  );
}

function Tab({ active, onClick, children }: { active: boolean; onClick: () => void; children: ReactNode }) {
  return (
    <button
      type="button"
      onClick={onClick}
      aria-pressed={active}
      className={cn(
        "flex items-center rounded-md px-3 py-1.5 text-sm font-medium transition-colors",
        active ? "bg-surface text-text shadow-xs" : "text-muted hover:text-text",
      )}
    >
      {children}
    </button>
  );
}

/**
 * How many drafts of this type are waiting.
 *
 * Shares its query key with the panel below, so a screen showing both the count and the list fetches
 * once — and discarding a draft updates the badge without anything having to remember to.
 */
export function useDraftCount(docType: DraftDocType): number {
  const drafts = useQuery({ queryKey: ["drafts", docType], queryFn: () => listDrafts(docType) });

  return drafts.data?.length ?? 0;
}

export interface DraftsPanelProps {
  docType: DraftDocType;
  /** The create screen a draft is resumed on — `/quotations/new`, and so on. */
  resumeHref: string;
  /** What one of these is called, lower case, for the empty state and the confirmations. */
  noun: string;
  /** "Customer" or "Supplier" — whichever party this document is addressed to. */
  partyLabel: string;
}

export function DraftsPanel({ docType, resumeHref, noun, partyLabel }: DraftsPanelProps) {
  const router = useRouter();
  const queryClient = useQueryClient();
  const [discarding, setDiscarding] = useState<DraftSummary | null>(null);

  const drafts = useQuery({ queryKey: ["drafts", docType], queryFn: () => listDrafts(docType) });
  const error = drafts.error as ApiError | null;

  const discard = useMutation({
    mutationFn: (draft: DraftSummary) => deleteDraft(draft.id),
    onSuccess: async () => {
      setDiscarding(null);
      toast.success(`Draft ${noun} discarded.`);
      await queryClient.invalidateQueries({ queryKey: ["drafts", docType] });
    },
  });
  const discardError = discard.error as ApiError | null;

  return (
    <>
      {error && <ErrorBanner message={error.message} correlationId={error.correlationId} />}
      {discardError && (
        <ErrorBanner message={discardError.message} correlationId={discardError.correlationId} />
      )}

      <DataTable
        columns={columns(partyLabel, setDiscarding)}
        rows={drafts.data}
        loading={drafts.isPending}
        searchable={(row) => `${row.partyName ?? ""} ${row.createdByName ?? ""}`}
        searchPlaceholder={`Search drafts by ${partyLabel.toLowerCase()} or who started it…`}
        defaultSort={{ id: "updated", desc: true }}
        onRowClick={(row) => router.push(`${resumeHref}?draft=${row.id}`)}
        empty={{
          title: "No drafts",
          description: `A ${noun} you start and do not raise is kept here, so you can pick it up later.`,
        }}
      />

      <Dialog
        open={discarding !== null}
        onOpenChange={(open) => !open && setDiscarding(null)}
        title={`Discard this draft ${noun}?`}
        description={
          discarding === null ? undefined : (
            <>
              {describe(discarding, partyLabel)} It has not been raised, so nothing is cancelled — but
              the work typed into it is deleted and cannot be recovered.
            </>
          )
        }
        footer={
          <>
            <Button variant="secondary" onClick={() => setDiscarding(null)}>
              Keep it
            </Button>
            <Button
              variant="danger"
              pending={discard.isPending}
              onClick={() => discarding !== null && discard.mutate(discarding)}
            >
              Discard draft
            </Button>
          </>
        }
      />
    </>
  );
}

function columns(
  partyLabel: string,
  onDiscard: (draft: DraftSummary) => void,
): ColumnDef<DraftSummary, unknown>[] {
  return [
    // Last edited leads, and the list opens with the most recent first: the draft somebody is coming
    // back to is almost always the one they were last in.
    {
      id: "updated",
      accessorFn: (row) => row.updatedAt,
      header: "Last edited",
      cell: ({ row }) => (
        <span className="whitespace-nowrap text-muted">{formatAgo(row.original.updatedAt)}</span>
      ),
    },
    {
      id: "party",
      accessorFn: (row) => row.partyName ?? "",
      header: partyLabel,
      cell: ({ row }) => (
        <span className="flex items-center gap-2">
          <FilePenLine className="size-4 shrink-0 text-muted" aria-hidden />
          <span className="font-medium text-text">
            {row.original.partyName ?? <span className="text-muted">Not chosen yet</span>}
          </span>
        </span>
      ),
    },
    {
      id: "lines",
      accessorFn: (row) => row.lineCount,
      header: "Lines",
      meta: { align: "right" },
      cell: ({ row }) => <span className="tabular text-muted">{row.original.lineCount}</span>,
    },
    {
      id: "total",
      accessorFn: (row) => row.total ?? 0,
      header: "Total so far",
      meta: { align: "right" },
      cell: ({ row }) => (
        <span className="tabular font-medium text-text">
          {row.original.total == null ? "—" : formatMoney(row.original.total)}
        </span>
      ),
    },
    {
      id: "who",
      accessorFn: (row) => row.updatedByName ?? row.createdByName ?? "",
      header: "Left by",
      // Whoever last typed into it — which, in a shared draft, is not always who started it.
      cell: ({ row }) => (
        <span className="text-muted">
          {row.original.updatedByName ?? row.original.createdByName ?? "—"}
        </span>
      ),
    },
    {
      id: "discard",
      header: "",
      meta: { align: "right" },
      enableSorting: false,
      cell: ({ row }) => (
        <button
          type="button"
          // The row itself resumes the draft; this must not do both.
          onClick={(e) => {
            e.stopPropagation();
            onDiscard(row.original);
          }}
          className="grid size-8 place-items-center rounded-md text-muted transition-colors hover:bg-surface-sunken hover:text-danger"
          aria-label={`Discard the draft for ${row.original.partyName ?? "no customer"}`}
        >
          <Trash2 className="size-4" />
        </button>
      ),
    },
  ];
}

function describe(draft: DraftSummary, partyLabel: string): string {
  const party = draft.partyName ?? `no ${partyLabel.toLowerCase()} chosen`;
  const lines = draft.lineCount === 1 ? "1 line" : `${draft.lineCount} lines`;

  return `${party} — ${lines}, last edited ${formatAgo(draft.updatedAt)}.`;
}

/**
 * "4 minutes ago" — how long a draft has been sitting, which is the question actually being asked.
 *
 * An absolute timestamp answers a different one. Someone scanning this list wants to know which draft is
 * the one they were in ten minutes ago, and "12:04" makes them do the subtraction themselves.
 */
function formatAgo(value: string): string {
  const at = instantFromApi(value);
  if (at === null) return "—";

  const seconds = Math.round((Date.now() - at.getTime()) / 1000);
  if (seconds < 60) return "just now";

  const relative = new Intl.RelativeTimeFormat(undefined, { numeric: "auto" });

  const HOUR = 3_600;
  const DAY = 86_400;

  if (seconds < HOUR) return relative.format(-Math.round(seconds / 60), "minute");
  if (seconds < DAY) return relative.format(-Math.round(seconds / HOUR), "hour");
  if (seconds < 7 * DAY) return relative.format(-Math.round(seconds / DAY), "day");

  // Past a week, "17 days ago" stops being easier to read than the date itself.
  return at.toLocaleDateString(undefined, { dateStyle: "medium" });
}
