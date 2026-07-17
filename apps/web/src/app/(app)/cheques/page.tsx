"use client";

/**
 * The cheque register (Phase 7, slice 2) — cheques written.
 *
 * A standalone list: this app's own cheques and the legacy ones adopted. No ledger, no balance. Printing is
 * Phase 8.
 */

import { useQuery } from "@tanstack/react-query";
import { useRouter } from "next/navigation";
import { Plus } from "lucide-react";
import { ApiError } from "@/lib/api";
import { getCheques, type ChequeSummary } from "@/lib/cheques";
import { PageHeader } from "@/components/shell/app-shell";
import { DataTable, type ColumnDef } from "@/components/data-table";
import { formatMoney, formatReportDate } from "@/components/reports";
import { Badge, Button, ErrorBanner, FadeIn } from "@/components/ui";

export default function ChequesPage() {
  const router = useRouter();
  const cheques = useQuery({ queryKey: ["cheques"], queryFn: getCheques });
  const error = cheques.error as ApiError | null;

  return (
    <FadeIn className="space-y-6">
      <PageHeader
        title="Cheques"
        description="Cheques written — this app's own and the legacy ones. A standalone register; printing arrives in Phase 8."
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
];
