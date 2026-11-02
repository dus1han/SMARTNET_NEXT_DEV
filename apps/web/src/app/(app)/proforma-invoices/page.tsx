"use client";

/**
 * The proforma invoice list.
 *
 * Every proforma, newest first — raised from a quotation, or entered on its own with New proforma. A proforma charges nothing — its status says where
 * it is in the chain: Open (waiting to be converted), or Invoiced (it became the invoice it links to).
 * Voided proformas sit behind their own tab: kept on record, but out of the way of the live ones.
 */

import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { useRouter } from "next/navigation";
import { useState, type ReactNode } from "react";
import { Plus } from "lucide-react";
import { ApiError } from "@/lib/api";
import { cn } from "@/lib/cn";
import { FIRST_PAGE } from "@/lib/paging";
import { getProformas, type ProformaSummary } from "@/lib/proformas";
import { PageHeader } from "@/components/shell/app-shell";
import { DataTable, type ColumnDef } from "@/components/data-table";
import { formatMoney, formatReportDate } from "@/components/reports";
import { Button, ErrorBanner, FadeIn } from "@/components/ui";
import { ProformaStatusBadge } from "@/components/documents/proforma-status";

export default function ProformaInvoicesPage() {
  const router = useRouter();
  const [page, setPage] = useState(FIRST_PAGE);
  const [search, setSearch] = useState("");
  const [voided, setVoided] = useState(false);

  const proformas = useQuery({
    queryKey: ["proformas", page, search, voided],
    queryFn: () => getProformas({ page, search, voided }),
    placeholderData: keepPreviousData,
  });
  const error = proformas.error as ApiError | null;

  // Switching tab is a different list, so it starts again from the first page.
  const show = (wantVoided: boolean) => {
    setVoided(wantVoided);
    setPage(FIRST_PAGE);
  };

  return (
    <FadeIn className="space-y-6">
      <PageHeader
        title="Proforma invoices"
        description="Raised from a quotation or on their own. A proforma charges nothing — the customer is billed when it is converted to an invoice."
      />

      <div className="flex w-fit items-center gap-1 rounded-lg border border-subtle bg-surface-sunken p-1">
        <Tab active={!voided} onClick={() => show(false)}>Proformas</Tab>
        <Tab active={voided} onClick={() => show(true)}>Voided</Tab>
      </div>

      {error && <ErrorBanner message={error.message} correlationId={error.correlationId} />}

      <DataTable
        columns={columns}
        rows={proformas.data?.rows}
        loading={proformas.isPending}
        searchable={(row) => `${row.number} ${row.quotationNumber ?? ""} ${row.customerName ?? ""}`}
        server={{
          total: proformas.data?.total ?? 0,
          page,
          onPageChange: setPage,
          search,
          onSearchChange: setSearch,
        }}
        searchPlaceholder="Search by proforma number, quotation number or customer…"
        actions={
          <Button size="sm" onClick={() => router.push("/proforma-invoices/new")}>
            <Plus />
            New proforma
          </Button>
        }
        defaultSort={{ id: "date", desc: true }}
        onRowClick={(row) => router.push(`/proforma-invoices/${row.id}`)}
        empty={
          voided
            ? { title: "No voided proformas", description: "A voided proforma is kept here for the record." }
            : {
                title: "No proforma invoices yet",
                description: "Use New proforma, or open a quotation and choose Convert to proforma.",
              }
        }
      />
    </FadeIn>
  );
}

function Tab({ active, onClick, children }: { active: boolean; onClick: () => void; children: ReactNode }) {
  return (
    <button
      type="button"
      onClick={onClick}
      aria-pressed={active}
      className={cn(
        "rounded-md px-3 py-1.5 text-sm font-medium transition-colors",
        active ? "bg-surface text-text shadow-xs" : "text-muted hover:text-text",
      )}
    >
      {children}
    </button>
  );
}

const columns: ColumnDef<ProformaSummary, unknown>[] = [
  {
    id: "date",
    accessorFn: (row) => row.date,
    header: "Date",
    cell: ({ row }) => <span className="whitespace-nowrap text-muted">{formatReportDate(row.original.date)}</span>,
  },
  {
    id: "number",
    accessorFn: (row) => row.number,
    header: "Proforma No",
    cell: ({ row }) => <span className="whitespace-nowrap font-medium text-text">{row.original.number}</span>,
  },
  {
    id: "quotation",
    accessorFn: (row) => row.quotationNumber ?? "",
    header: "Quotation",
    cell: ({ row }) => <span className="whitespace-nowrap text-muted">{row.original.quotationNumber ?? "—"}</span>,
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
    accessorFn: (row) => row.status,
    header: "Status",
    cell: ({ row }) => <ProformaStatusBadge status={row.original.status} />,
  },
];
