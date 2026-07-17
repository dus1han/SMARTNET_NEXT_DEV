"use client";

/**
 * The supplier-payments list — money out (Phase 7).
 *
 * The payables mirror of the customer Payments page. A payment is money paid to a supplier, allocated across
 * one or more open supplier invoices. Each allocation posts a Payment entry to the payables ledger (the
 * truth), dual-writing the legacy supplier_inv_pay row and paymentstat for the surviving report.
 */

import { useQuery } from "@tanstack/react-query";
import { useRouter } from "next/navigation";
import { Plus } from "lucide-react";
import { ApiError } from "@/lib/api";
import { getSupplierPayments, type SupplierPaymentSummary } from "@/lib/supplier-payments";
import { PageHeader } from "@/components/shell/app-shell";
import { DataTable, type ColumnDef } from "@/components/data-table";
import { formatMoney, formatReportDate } from "@/components/reports";
import { Badge, Button, ErrorBanner, FadeIn } from "@/components/ui";

export default function SupplierPaymentsPage() {
  const router = useRouter();
  const payments = useQuery({ queryKey: ["supplier-payments"], queryFn: getSupplierPayments });
  const error = payments.error as ApiError | null;

  return (
    <FadeIn className="space-y-6">
      <PageHeader
        title="Supplier payments"
        description="Money paid to suppliers, newest first. A payment settles one or more invoices — the allocation is on the payables ledger."
      />

      {error && <ErrorBanner message={error.message} correlationId={error.correlationId} />}

      <DataTable
        columns={columns}
        rows={payments.data}
        loading={payments.isPending}
        searchable={(row) => `${row.supplierName ?? ""} ${row.reference ?? ""} ${row.method ?? ""}`}
        searchPlaceholder="Search by supplier, reference or method…"
        defaultSort={{ id: "date", desc: true }}
        actions={
          <Button size="sm" onClick={() => router.push("/supplier-payments/new")}>
            <Plus />
            Record a payment
          </Button>
        }
        onRowClick={(row) => router.push(`/supplier-payments/${row.id}`)}
        empty={{
          title: "No supplier payments yet",
          description: "Money paid to suppliers — this app's own and the legacy ones — appears here.",
        }}
      />
    </FadeIn>
  );
}

const columns: ColumnDef<SupplierPaymentSummary, unknown>[] = [
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
    cell: ({ row }) => (
      <span className="flex items-center gap-2">
        <span className="font-medium text-text">{row.original.supplierName ?? "—"}</span>
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
