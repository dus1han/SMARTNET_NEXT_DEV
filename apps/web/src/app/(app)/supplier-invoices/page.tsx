"use client";

/**
 * The supplier-invoice list — accounts payable.
 *
 * What this app has recorded and the legacy ones adopted. Outstanding and status are derived from the
 * payables ledger (never a stored, mutated column), so partial payments show real headroom and "Paid" is
 * a computed fact, not the legacy binary flag.
 */

import { useQuery } from "@tanstack/react-query";
import { useRouter } from "next/navigation";
import { Plus } from "lucide-react";
import { ApiError } from "@/lib/api";
import { getSupplierInvoices, type SupplierInvoiceSummary } from "@/lib/supplier-invoices";
import { PageHeader } from "@/components/shell/app-shell";
import { DataTable, type ColumnDef } from "@/components/data-table";
import { formatMoney, formatReportDate } from "@/components/reports";
import { Badge, Button, ErrorBanner, FadeIn } from "@/components/ui";

export default function SupplierInvoicesPage() {
  const router = useRouter();
  const invoices = useQuery({ queryKey: ["supplier-invoices"], queryFn: getSupplierInvoices });
  const error = invoices.error as ApiError | null;

  return (
    <FadeIn className="space-y-6">
      <PageHeader
        title="Supplier invoices"
        description="What we owe suppliers, newest first. Outstanding and status are derived from the payables ledger — partial payments and all."
      />

      {error && <ErrorBanner message={error.message} correlationId={error.correlationId} />}

      <DataTable
        columns={columns}
        rows={invoices.data}
        loading={invoices.isPending}
        searchable={(row) => `${row.supplierReference ?? ""} ${row.supplierName ?? ""}`}
        searchPlaceholder="Search by reference or supplier…"
        defaultSort={{ id: "date", desc: true }}
        actions={
          <Button size="sm" onClick={() => router.push("/supplier-invoices/new")}>
            <Plus />
            New supplier invoice
          </Button>
        }
        onRowClick={(row) => router.push(`/supplier-invoices/${row.id}`)}
        empty={{
          title: "No supplier invoices yet",
          description: "Supplier invoices recorded in the new system appear here.",
        }}
      />
    </FadeIn>
  );
}

const columns: ColumnDef<SupplierInvoiceSummary, unknown>[] = [
  {
    id: "reference",
    accessorFn: (row) => row.supplierReference ?? "",
    header: "Reference",
    cell: ({ row }) => (
      <span className="flex items-center gap-2">
        <span className="font-medium text-text">{row.original.supplierReference || "—"}</span>
        {row.original.origin === "legacy" && <Badge tone="neutral">Legacy</Badge>}
      </span>
    ),
  },
  {
    id: "date",
    accessorFn: (row) => row.date,
    header: "Date",
    cell: ({ row }) => <span className="whitespace-nowrap text-muted">{formatReportDate(row.original.date)}</span>,
  },
  {
    id: "supplier",
    accessorFn: (row) => row.supplierName ?? "",
    header: "Supplier",
    cell: ({ row }) => <span className="text-text">{row.original.supplierName ?? "—"}</span>,
  },
  {
    id: "amount",
    accessorFn: (row) => row.amount,
    header: "Amount",
    meta: { align: "right" },
    cell: ({ row }) => <span className="tabular font-medium text-text">{formatMoney(row.original.amount)}</span>,
  },
  {
    id: "outstanding",
    accessorFn: (row) => row.outstanding,
    header: "Outstanding",
    meta: { align: "right" },
    cell: ({ row }) => <span className="tabular text-text">{formatMoney(row.original.outstanding)}</span>,
  },
  {
    id: "status",
    accessorFn: (row) => row.status,
    header: "Status",
    cell: ({ row }) => (
      <Badge tone={row.original.status === "Paid" ? "success" : "warning"}>{row.original.status}</Badge>
    ),
  },
];
