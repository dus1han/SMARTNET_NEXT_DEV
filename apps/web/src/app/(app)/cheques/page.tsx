"use client";

/**
 * The cheque register (Phase 7, slice 2) — cheques written.
 *
 * A standalone list: this app's own cheques and the legacy ones adopted. No ledger, no balance.
 *
 * Each row prints from here. The register is where somebody stands with the stationery already in the
 * printer, so making them open the cheque first was a step that bought nothing.
 */

import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useRouter } from "next/navigation";
import { useState } from "react";
import { Plus, Printer } from "lucide-react";
import { ApiError } from "@/lib/api";
import { getCheques, type ChequeSummary } from "@/lib/cheques";
import { PageHeader } from "@/components/shell/app-shell";
import { DataTable, type ColumnDef } from "@/components/data-table";
import { formatMoney, formatReportDate } from "@/components/reports";
import { PrintPreview } from "@/components/print-preview";
import { Badge, Button, ErrorBanner, FadeIn } from "@/components/ui";

/**
 * The print button for one row of the register.
 *
 * Its own component because each row needs its own open/closed state — one shared flag would open
 * every preview at once, and the register is the screen most likely to print several in a sitting.
 *
 * Fetching the preview is what records the print, so the list is invalidated when it loads and the
 * count in the next column moves without a refresh.
 */
function PrintCell({ cheque }: { cheque: ChequeSummary }) {
  const [printing, setPrinting] = useState(false);
  const queryClient = useQueryClient();

  return (
    <>
      <Button
        variant="ghost"
        size="sm"
        onClick={(e) => {
          e.stopPropagation();
          setPrinting(true);
        }}
      >
        <Printer />
        Print
      </Button>

      <div onClick={(e) => e.stopPropagation()}>
        <PrintPreview
        open={printing}
        onOpenChange={setPrinting}
        path={`/api/cheques/${cheque.id}/pdf`}
        title={`Cheque ${cheque.chequeNumber ?? ""}`.trim()}
        onLoaded={() => {
          void queryClient.invalidateQueries({ queryKey: ["cheques"] });
          void queryClient.invalidateQueries({ queryKey: ["cheque", cheque.id] });
          void queryClient.invalidateQueries({ queryKey: ["history", "Cheque", String(cheque.id)] });
        }}
        />
      </div>
    </>
  );
}

export default function ChequesPage() {
  const router = useRouter();
  const cheques = useQuery({ queryKey: ["cheques"], queryFn: getCheques });
  const error = cheques.error as ApiError | null;

  return (
    <FadeIn className="space-y-6">
      <PageHeader
        title="Cheques"
        description="Cheques written — this app's own and the legacy ones. Print one straight onto its stationery; every print is logged."
      />

      {error && <ErrorBanner message={error.message} correlationId={error.correlationId} />}

      <DataTable
        columns={columns}
        rows={cheques.data}
        loading={cheques.isPending}
        searchable={(row) => `${row.payTo} ${row.bank ?? ""} ${row.chequeNumber ?? ""}`}
        searchPlaceholder="Search by payee, bank or cheque no…"
        defaultSort={{ id: "chequeDate", desc: true }}
        actions={
          <Button size="sm" onClick={() => router.push("/cheques/new")}>
            <Plus />
            Record a cheque
          </Button>
        }
        onRowClick={(row) => router.push(`/cheques/${row.id}`)}
        empty={{
          title: "No cheques yet",
          description: "Cheques recorded in the new system — and the legacy ones — appear here.",
        }}
      />
    </FadeIn>
  );
}

const columns: ColumnDef<ChequeSummary, unknown>[] = [
  {
    id: "chequeDate",
    accessorFn: (row) => row.chequeDate ?? "",
    header: "Cheque date",
    cell: ({ row }) => <span className="whitespace-nowrap text-muted">{row.original.chequeDate ? formatReportDate(row.original.chequeDate) : "—"}</span>,
  },
  {
    id: "payTo",
    accessorFn: (row) => row.payTo,
    header: "Pay to",
    cell: ({ row }) => (
      <span className="flex items-center gap-2">
        <span className="font-medium text-text">{row.original.payTo || "—"}</span>
        {row.original.origin === "legacy" && <Badge tone="neutral">Legacy</Badge>}
      </span>
    ),
  },
  {
    id: "source",
    accessorFn: (row) => row.source,
    header: "Source",
    cell: ({ row }) => (
      <Badge tone={row.original.source === "Manual" ? "neutral" : "success"}>{row.original.source}</Badge>
    ),
  },
  {
    id: "bank",
    accessorFn: (row) => row.bank ?? "",
    header: "Bank",
    cell: ({ row }) => <span className="text-text">{row.original.bank || "—"}</span>,
  },
  {
    id: "chequeNumber",
    accessorFn: (row) => row.chequeNumber ?? "",
    header: "Cheque no.",
    cell: ({ row }) => <span className="tabular text-muted">{row.original.chequeNumber || "—"}</span>,
  },
  {
    id: "dueDate",
    accessorFn: (row) => row.dueDate ?? "",
    header: "Due date",
    cell: ({ row }) => <span className="whitespace-nowrap text-muted">{row.original.dueDate ? formatReportDate(row.original.dueDate) : "—"}</span>,
  },
  {
    id: "amount",
    accessorFn: (row) => row.amount,
    header: "Amount",
    meta: { align: "right" },
    cell: ({ row }) => <span className="tabular font-medium text-text">{formatMoney(row.original.amount)}</span>,
  },
  {
    id: "print",
    header: "",
    enableSorting: false,
    meta: { align: "right" },
    cell: ({ row }) => <PrintCell cheque={row.original} />,
  },
  {
    id: "printed",
    accessorFn: (row) => row.printCount,
    header: "Printed",
    meta: { align: "center" },
    // Reprints are allowed, so the count is the thing worth seeing: a cheque printed more than once is
    // ordinary, but it should never be a surprise. A legacy cheque's prints predate the audit trail, so
    // it reads "—" with its last-printed date in the tooltip rather than a count it cannot know.
    cell: ({ row }) => {
      const { printCount, lastPrintedAt, origin } = row.original;

      if (printCount === 0) {
        return (
          <span className="text-muted" title={lastPrintedAt ? `Last printed ${formatReportDate(lastPrintedAt)} in the legacy system` : undefined}>
            {origin === "legacy" && lastPrintedAt ? "—" : "Not printed"}
          </span>
        );
      }

      return (
        <Badge tone={printCount > 1 ? "warning" : "neutral"} title={lastPrintedAt ? `Last printed ${formatReportDate(lastPrintedAt)}` : undefined}>
          {printCount}×
        </Badge>
      );
    },
  },
];
