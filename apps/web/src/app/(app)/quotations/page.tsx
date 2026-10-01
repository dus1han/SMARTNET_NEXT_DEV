"use client";

/**
 * The quotation list.
 *
 * The quotations this app has raised. A quotation charges nothing, so there is no outstanding column;
 * what it shows instead is whether it has been converted into an invoice yet.
 *
 * Behind the Drafts tab is the other half: quotations that have been typed but not raised. They are kept
 * apart rather than mixed in, because a draft has no number, no status and no place in any report — see
 * `DocumentViewFilter`.
 *
 * The Items tab is the same raised quotations taken apart: one row per line, for finding what was quoted
 * and at what price without opening each quotation in turn.
 */

import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { useRouter } from "next/navigation";
import { useState } from "react";
import { Plus } from "lucide-react";
import { ApiError } from "@/lib/api";
import { FIRST_PAGE } from "@/lib/paging";
import { DRAFT_QUOTATION } from "@/lib/drafts";
import { listCustomers } from "@/lib/customers";
import { getQuotationLines, getQuotations, type QuotationLineSummary, type QuotationSummary } from "@/lib/quotations";
import { PageHeader } from "@/components/shell/app-shell";
import { DataTable, type ColumnDef } from "@/components/data-table";
import { DocumentViewFilter, DraftsPanel, type DocumentView } from "@/components/documents/drafts-panel";
import { CustomerCombobox } from "@/components/documents/line-draft";
import { formatMoney, formatReportDate } from "@/components/reports";
import { Badge, Button, ErrorBanner, FadeIn } from "@/components/ui";

export default function QuotationsPage() {
  const router = useRouter();
  const [page, setPage] = useState(FIRST_PAGE);
  const [search, setSearch] = useState("");
  const [view, setView] = useState<DocumentView>("issued");

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

      <DocumentViewFilter
        view={view}
        onChange={setView}
        docType={DRAFT_QUOTATION}
        issuedLabel="Quotations"
        linesLabel="Items"
      />

      {view === "drafts" ? (
        <DraftsPanel
          docType={DRAFT_QUOTATION}
          resumeHref="/quotations/new"
          noun="quotation"
          partyLabel="Customer"
          printable
        />
      ) : view === "lines" ? (
        <QuotationLinesPanel />
      ) : (
        <>
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
        </>
      )}
    </FadeIn>
  );
}

/**
 * The item-level view: every line of every raised quotation, paged and searched on the server — or, with
 * a customer chosen, only the lines quoted to that customer.
 */
function QuotationLinesPanel() {
  const router = useRouter();
  const [page, setPage] = useState(FIRST_PAGE);
  const [search, setSearch] = useState("");
  const [customerId, setCustomerId] = useState("");

  const customers = useQuery({ queryKey: ["customers"], queryFn: listCustomers });
  const lines = useQuery({
    queryKey: ["quotation-lines", page, search, customerId],
    queryFn: () => getQuotationLines({ page, search, customerId: customerId ? Number(customerId) : undefined }),
    placeholderData: keepPreviousData,
  });
  const error = lines.error as ApiError | null;

  // A different customer is a different list, so it starts again from its first page.
  const chooseCustomer = (id: string) => {
    setCustomerId(id);
    setPage(FIRST_PAGE);
  };

  return (
    <>
      {error && <ErrorBanner message={error.message} correlationId={error.correlationId} />}

      <div className="flex max-w-xl items-end gap-2">
        <div className="flex-1">
          <CustomerCombobox customers={customers.data ?? []} value={customerId} onChange={chooseCustomer} />
        </div>
        {customerId && (
          <Button variant="secondary" onClick={() => chooseCustomer("")}>
            All customers
          </Button>
        )}
      </div>

      <DataTable
        columns={lineColumns}
        rows={lines.data?.rows}
        loading={lines.isPending}
        searchable={(row) => `${row.quotationNumber} ${row.description ?? ""}`}
        server={{
          total: lines.data?.total ?? 0,
          page,
          onPageChange: setPage,
          search,
          onSearchChange: setSearch,
        }}
        searchPlaceholder="Search by quotation number or description…"
        defaultSort={{ id: "date", desc: true }}
        onRowClick={(row) => router.push(`/quotations/${row.quotationId}`)}
        empty={{
          title: "No quotation items",
          description: "The lines of raised quotations appear here.",
        }}
      />
    </>
  );
}

const lineColumns: ColumnDef<QuotationLineSummary, unknown>[] = [
  {
    id: "date",
    accessorFn: (row) => row.date,
    header: "Date",
    cell: ({ row }) => <span className="whitespace-nowrap text-muted">{formatReportDate(row.original.date)}</span>,
  },
  {
    id: "number",
    accessorFn: (row) => row.quotationNumber,
    header: "Quotation No",
    cell: ({ row }) => <span className="whitespace-nowrap font-medium text-text">{row.original.quotationNumber}</span>,
  },
  {
    id: "description",
    accessorFn: (row) => row.description ?? "",
    header: "Description",
    cell: ({ row }) => <span className="text-text">{row.original.description || "—"}</span>,
  },
  {
    id: "quantity",
    accessorFn: (row) => row.quantity,
    header: "Qty",
    meta: { align: "right" },
    cell: ({ row }) => <span className="tabular text-text">{row.original.quantity}</span>,
  },
  {
    id: "unitPrice",
    accessorFn: (row) => row.unitPrice,
    header: "Unit price",
    meta: { align: "right" },
    cell: ({ row }) => <span className="tabular text-text">{formatMoney(row.original.unitPrice)}</span>,
  },
  {
    id: "discount",
    accessorFn: (row) => row.discountPercent,
    header: "Disc %",
    meta: { align: "right" },
    cell: ({ row }) => <span className="tabular text-muted">{row.original.discountPercent}</span>,
  },
  {
    id: "net",
    accessorFn: (row) => row.net,
    header: "Net",
    meta: { align: "right" },
    cell: ({ row }) => <span className="tabular font-medium text-text">{formatMoney(row.original.net)}</span>,
  },
];

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
