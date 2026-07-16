"use client";

/**
 * The quotation list.
 *
 * The quotations this app has raised. A quotation charges nothing, so there is no outstanding column;
 * what it shows instead is whether it has been converted into an invoice yet.
 */

import { useQuery } from "@tanstack/react-query";
import { useRouter } from "next/navigation";
import { Plus } from "lucide-react";
import { ApiError } from "@/lib/api";
import { getQuotations, type QuotationSummary } from "@/lib/quotations";
import { PageHeader } from "@/components/shell/app-shell";
import { DataTable, type ColumnDef } from "@/components/data-table";
import { formatMoney, formatReportDate } from "@/components/reports";
import { Badge, Button, ErrorBanner, FadeIn } from "@/components/ui";

export default function QuotationsPage() {
  const router = useRouter();
  const quotations = useQuery({ queryKey: ["quotations"], queryFn: getQuotations });
  const error = quotations.error as ApiError | null;

  return (
    <FadeIn className="space-y-6">
      <PageHeader
        title="Quotations"
        description="Every quotation raised in the new system, newest first. A quotation is a priced offer — it charges nothing until converted."
      />

      {error && <ErrorBanner message={error.message} correlationId={error.correlationId} />}

      <DataTable
        columns={columns}
        rows={quotations.data}
        loading={quotations.isPending}
        searchable={(row) => `${row.number} ${row.customerName ?? ""}`}
        searchPlaceholder="Search by number or customer…"
        defaultSort={{ id: "number", desc: true }}
        actions={
          <Button size="sm" onClick={() => router.push("/quotations/new")}>
            <Plus />
            New quotation
          </Button>
        }
        onRowClick={(row) => router.push(`/quotations/${row.id}`)}
        empty={{
          title: "No quotations yet",
          description: "Quotations raised in the new system appear here.",
        }}
      />
    </FadeIn>
  );
}

const columns: ColumnDef<QuotationSummary, unknown>[] = [
  {
    id: "number",
    accessorFn: (row) => row.number,
    header: "Number",
    cell: ({ row }) => (
      <span className="flex items-center gap-2">
        <span className="font-medium text-text">{row.original.number}</span>
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
    id: "customer",
    accessorFn: (row) => row.customerName ?? "",
    header: "Customer",
    cell: ({ row }) => <span className="text-text">{row.original.customerName ?? "—"}</span>,
  },
  {
    id: "total",
    accessorFn: (row) => row.total,
    header: "Total",
    cell: ({ row }) => <span className="tabular font-medium text-text">{formatMoney(row.original.total)}</span>,
  },
  {
    id: "status",
    accessorFn: (row) => (row.convertedInvoiceId == null ? "Open" : "Converted"),
    header: "Status",
    cell: ({ row }) => {
      // Open until converted — including legacy quotes, which can now be converted through the new app.
      const converted = row.original.convertedInvoiceId != null;
      return <Badge tone={converted ? "success" : "neutral"}>{converted ? "Converted" : "Open"}</Badge>;
    },
  },
];
