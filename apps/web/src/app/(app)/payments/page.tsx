"use client";

/**
 * The customer-receipts list — money in (Phase 7 slice 1).
 *
 * A receipt is money received from a customer, allocated across one or more open invoices. Each allocation
 * posts a Payment entry to the receivables ledger (the truth), dual-writing the legacy payments row and
 * invoice_h.balance for the surviving outstanding report.
 */

import { useQuery } from "@tanstack/react-query";
import { useRouter } from "next/navigation";
import { Plus } from "lucide-react";
import { ApiError } from "@/lib/api";
import { getCustomerReceipts, type CustomerReceiptSummary } from "@/lib/payments";
import { PageHeader } from "@/components/shell/app-shell";
import { DataTable, type ColumnDef } from "@/components/data-table";
import { formatMoney, formatReportDate } from "@/components/reports";
import { Badge, Button, ErrorBanner, FadeIn } from "@/components/ui";

export default function PaymentsPage() {
  const router = useRouter();
  const receipts = useQuery({ queryKey: ["customer-receipts"], queryFn: getCustomerReceipts });
  const error = receipts.error as ApiError | null;

  return (
    <FadeIn className="space-y-6">
      <PageHeader
        title="Payments"
        description="Money received from customers, newest first. A receipt settles one or more invoices — the allocation is on the receivables ledger."
      />

      {error && <ErrorBanner message={error.message} correlationId={error.correlationId} />}

      <DataTable
        columns={columns}
        rows={receipts.data}
        loading={receipts.isPending}
        searchable={(row) => `${row.customerName ?? ""} ${row.reference ?? ""} ${row.method ?? ""}`}
        searchPlaceholder="Search by customer, reference or method…"
        defaultSort={{ id: "date", desc: true }}
        actions={
          <Button size="sm" onClick={() => router.push("/payments/new")}>
            <Plus />
            Record a receipt
          </Button>
        }
        onRowClick={(row) => row.origin === "new" && router.push(`/payments/${row.id}`)}
        empty={{
          title: "No receipts yet",
          description: "Money received from customers — this app's own and the legacy ones — appears here.",
        }}
      />
    </FadeIn>
  );
}

const columns: ColumnDef<CustomerReceiptSummary, unknown>[] = [
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
    cell: ({ row }) => (
      <span className="flex items-center gap-2">
        <span className="font-medium text-text">{row.original.customerName ?? "—"}</span>
        {row.original.origin === "legacy" && <Badge tone="neutral">Legacy</Badge>}
      </span>
    ),
  },
  {
    id: "method",
    accessorFn: (row) => row.method ?? "",
    header: "Method",
    cell: ({ row }) => <span className="text-text">{row.original.method || "—"}</span>,
  },
  {
    id: "reference",
    accessorFn: (row) => row.reference ?? "",
    header: "Reference",
    cell: ({ row }) => <span className="text-muted">{row.original.reference || "—"}</span>,
  },
  {
    id: "invoices",
    accessorFn: (row) => row.invoices,
    header: "Invoices",
    meta: { align: "center" },
    cell: ({ row }) => <span className="tabular text-muted">{row.original.invoices}</span>,
  },
  {
    id: "amount",
    accessorFn: (row) => row.amount,
    header: "Amount",
    meta: { align: "right" },
    cell: ({ row }) => <span className="tabular font-medium text-text">{formatMoney(row.original.amount)}</span>,
  },
];
