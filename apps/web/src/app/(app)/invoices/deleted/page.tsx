"use client";

/**
 * The deleted-invoice register (deleted_in) — the new-side replacement for the legacy
 * DeletedInvoicesController.
 *
 * Voided invoices, newest first, with who voided each, when and why (the reason from the audit trail).
 * Nothing here was erased — a void is a soft delete, so every one of these rows still exists in full and
 * could be restored from its History tab.
 */

import { useQuery } from "@tanstack/react-query";
import { ApiError } from "@/lib/api";
import { getDeletedInvoices, type DeletedInvoiceSummary } from "@/lib/invoices";
import { PageHeader } from "@/components/shell/app-shell";
import { DataTable, type ColumnDef } from "@/components/data-table";
import { formatMoney, formatReportDate } from "@/components/reports";
import { ErrorBanner, FadeIn } from "@/components/ui";

export default function DeletedInvoicesPage() {
  const deleted = useQuery({ queryKey: ["deleted-invoices"], queryFn: getDeletedInvoices });
  const error = deleted.error as ApiError | null;

  return (
    <FadeIn className="space-y-6">
      <PageHeader
        title="Deleted invoices"
        description="Voided invoices, with who voided each, when and why. A void is a soft delete — nothing is erased, and every one can be restored."
      />

      {error && <ErrorBanner message={error.message} correlationId={error.correlationId} />}

      <DataTable
        columns={columns}
        rows={deleted.data}
        loading={deleted.isPending}
        searchable={(row) => `${row.number} ${row.customerName ?? ""} ${row.reason ?? ""}`}
        searchPlaceholder="Search by number, customer or reason…"
        defaultSort={{ id: "deletedAt", desc: true }}
        empty={{ title: "Nothing voided", description: "Invoices that are voided appear here." }}
      />
    </FadeIn>
  );
}

const columns: ColumnDef<DeletedInvoiceSummary, unknown>[] = [
  {
    id: "number",
    accessorFn: (row) => row.number,
    header: "Number",
    cell: ({ row }) => <span className="font-medium text-text">{row.original.number}</span>,
  },
  {
    id: "customer",
    accessorFn: (row) => row.customerName ?? "",
    header: "Customer",
    cell: ({ row }) => <span className="text-text">{row.original.customerName ?? "—"}</span>,
  },
  {
    id: "total",
    accessorFn: (row) => row.total,
    header: "Total",
    cell: ({ row }) => <span className="tabular text-muted">{formatMoney(row.original.total)}</span>,
  },
  {
    id: "deletedAt",
    accessorFn: (row) => row.deletedAt,
    header: "Voided",
    cell: ({ row }) => (
      <span className="whitespace-nowrap text-muted">
        {formatReportDate(row.original.deletedAt)}
        {row.original.deletedByName && <span className="ml-2 text-xs">by {row.original.deletedByName}</span>}
      </span>
    ),
  },
  {
    id: "reason",
    accessorFn: (row) => row.reason ?? "",
    header: "Reason",
    enableSorting: false,
    cell: ({ row }) => <span className="text-sm text-muted">{row.original.reason || "—"}</span>,
  },
];
