"use client";

/**
 * The quotation list.
 *
 * The quotations this app has raised. A quotation charges nothing, so there is no outstanding column;
 * what it shows instead is whether it has been converted into an invoice yet.
 */

import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { useRouter } from "next/navigation";
import { useState } from "react";
import { Plus } from "lucide-react";
import { ApiError } from "@/lib/api";
import { FIRST_PAGE } from "@/lib/paging";
import { getQuotations, type QuotationSummary } from "@/lib/quotations";
import { PageHeader } from "@/components/shell/app-shell";
import { DataTable, type ColumnDef } from "@/components/data-table";
import { formatMoney, formatReportDate } from "@/components/reports";
import { Badge, Button, ErrorBanner, FadeIn } from "@/components/ui";

export default function QuotationsPage() {
  const router = useRouter();
  const [page, setPage] = useState(FIRST_PAGE);
  const [search, setSearch] = useState("");

  const quotations = useQuery({
    queryKey: ["quotations", page, search],
    queryFn: () => getQuotations({ page, search }),
    // Holds the current page on screen while the next loads, so paging does not blink.
    placeholderData: keepPreviousData,
  });
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
        rows={quotations.data?.rows}
        loading={quotations.isPending}
        searchable={(row) => `${row.number} ${row.customerName ?? ""}`}
        server={{
          total: quotations.data?.total ?? 0,
          page,
          onPageChange: setPage,
          search,
          onSearchChange: setSearch,
        }}
        searchPlaceholder="Search by number or customer…"
        defaultSort={{ id: "date", desc: true }}
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
  // Date leads and the list opens newest-first: a quotation is looked for by when it was raised far
  // more often than by its number.
  {
    id: "date",
    accessorFn: (row) => row.date,
    header: "Date",
    cell: ({ row }) => <span className="whitespace-nowrap text-muted">{formatReportDate(row.original.date)}</span>,
  },
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
    id: "customer",
    accessorFn: (row) => row.customerName ?? "",
    header: "Customer",
    cell: ({ row }) => <span className="text-text">{row.original.customerName ?? "—"}</span>,
  },
  {
    id: "total",
    accessorFn: (row) => row.total,
    header: "Total",
    meta: { align: "right" },
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
