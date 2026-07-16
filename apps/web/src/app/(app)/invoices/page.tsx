"use client";

/**
 * The invoice list.
 *
 * The invoices this app has raised — the new documents engine's first list screen. Outstanding is
 * derived from the receivables ledger on the server, never a stored column (B3); a cash invoice reads
 * as settled because its charge and its at-issue payment net to zero.
 */

import { useQuery } from "@tanstack/react-query";
import { useRouter } from "next/navigation";
import { ApiError } from "@/lib/api";
import { getInvoices, type InvoiceSummary } from "@/lib/invoices";
import { PageHeader } from "@/components/shell/app-shell";
import { Plus } from "lucide-react";
import { DataTable, type ColumnDef } from "@/components/data-table";
import { formatMoney, formatReportDate } from "@/components/reports";
import { Badge, Button, ErrorBanner, FadeIn } from "@/components/ui";

export default function InvoicesPage() {
  const router = useRouter();
  const invoices = useQuery({ queryKey: ["invoices"], queryFn: getInvoices });
  const error = invoices.error as ApiError | null;

  return (
    <FadeIn className="space-y-6">
      <PageHeader
        title="Invoices"
        description="Every invoice raised in the new system, newest first. The outstanding figure is derived from the ledger."
      />

      {error && <ErrorBanner message={error.message} correlationId={error.correlationId} />}

      <DataTable
        columns={columns}
        rows={invoices.data}
        loading={invoices.isPending}
        searchable={(row) => `${row.number} ${row.customerName ?? ""}`}
        searchPlaceholder="Search by number or customer…"
        defaultSort={{ id: "number", desc: true }}
        actions={
          <Button size="sm" onClick={() => router.push("/invoices/new")}>
            <Plus />
            New invoice
          </Button>
        }
        onRowClick={(row) => router.push(`/invoices/${row.id}`)}
        empty={{
          title: "No invoices yet",
          description: "Invoices raised in the new system appear here.",
        }}
      />
    </FadeIn>
  );
}

const columns: ColumnDef<InvoiceSummary, unknown>[] = [
  {
    id: "number",
    accessorFn: (row) => row.number,
    header: "Number",
    cell: ({ row }) => <span className="font-medium text-text">{row.original.number}</span>,
  },
  {
    id: "date",
    accessorFn: (row) => row.date,
    header: "Date",
    cell: ({ row }) => <span className="whitespace-nowrap text-muted">{formatReportDate(row.original.date)}</span>,
  },
  {
    id: "customer",
    accessorFn: (row) => row.customerName ?? "",
    header: "Customer",
    cell: ({ row }) => <span className="text-text">{row.original.customerName ?? "—"}</span>,
  },
  {
    id: "type",
    accessorFn: (row) => row.type,
    header: "Type",
    cell: ({ row }) => <Badge tone="neutral">{row.original.type}</Badge>,
  },
  {
    id: "total",
    accessorFn: (row) => row.total,
    header: "Total",
    cell: ({ row }) => <span className="tabular font-medium text-text">{formatMoney(row.original.total)}</span>,
  },
  {
    id: "outstanding",
    accessorFn: (row) => row.outstanding,
    header: "Outstanding",
    cell: ({ row }) => {
      // Derived: zero means the ledger says it is settled (a paid credit invoice, or any cash one).
      const settled = row.original.outstanding <= 0;
      return (
        <span className="flex items-center justify-end gap-2">
          <span className="tabular text-text">{formatMoney(row.original.outstanding)}</span>
          <Badge tone={settled ? "success" : "warning"}>{settled ? "Paid" : "Due"}</Badge>
        </span>
      );
    },
  },
];
