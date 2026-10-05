"use client";

/**
 * The Items view behind a list screen's Items tab — every line of every raised document, one row per
 * line, paged and searched on the server, and narrowed to one customer when one is chosen.
 *
 * One component for quotations and invoices, because the view is the same in both: what was offered or
 * sold, to whom and at what price, without opening each document in turn. What differs — which endpoint,
 * what the document is called, where a row opens — is passed in.
 */

import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { useRouter } from "next/navigation";
import { useState } from "react";
import { ApiError } from "@/lib/api";
import { listCustomers } from "@/lib/customers";
import { FIRST_PAGE, type Paged } from "@/lib/paging";
import { DataTable, type ColumnDef } from "@/components/data-table";
import { formatMoney, formatReportDate } from "@/components/reports";
import { Button, ErrorBanner } from "@/components/ui";
import { CustomerCombobox } from "./line-draft";

/** One line, as the Items view shows it — each document's own line type mapped to this. */
export interface DocumentLineRow {
  id: number;
  /** The document the line belongs to — what a click opens. */
  documentId: number;
  documentNumber: string;
  date: string;
  description?: string | null;
  quantity: number;
  unitPrice: number;
  discountPercent: number;
  net: number;
}

export interface LinesPanelProps {
  /** The query key's first part — distinct per document, so the two lists never share a cache entry. */
  queryKey: string;
  fetchPage: (params: { page: number; search: string; customerId?: number }) => Promise<Paged<DocumentLineRow>>;
  /** What one of these is called, capitalised — "Quotation", "Invoice". */
  noun: string;
  /** The read view a row opens, given the document's id. */
  hrefFor: (documentId: number) => string;
}

export function LinesPanel({ queryKey, fetchPage, noun, hrefFor }: LinesPanelProps) {
  const router = useRouter();
  const [page, setPage] = useState(FIRST_PAGE);
  const [search, setSearch] = useState("");
  const [customerId, setCustomerId] = useState("");

  const customers = useQuery({ queryKey: ["customers"], queryFn: listCustomers });
  const lines = useQuery({
    queryKey: [queryKey, page, search, customerId],
    queryFn: () => fetchPage({ page, search, customerId: customerId ? Number(customerId) : undefined }),
    placeholderData: keepPreviousData,
  });
  const error = lines.error as ApiError | null;

  // A different customer is a different list, so it starts again from its first page.
  const chooseCustomer = (id: string) => {
    setCustomerId(id);
    setPage(FIRST_PAGE);
  };

  const lower = noun.toLowerCase();

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
        columns={columns(noun)}
        rows={lines.data?.rows}
        loading={lines.isPending}
        searchable={(row) => `${row.documentNumber} ${row.description ?? ""}`}
        server={{
          total: lines.data?.total ?? 0,
          page,
          onPageChange: setPage,
          search,
          onSearchChange: setSearch,
        }}
        searchPlaceholder={`Search by ${lower} number or description…`}
        defaultSort={{ id: "date", desc: true }}
        onRowClick={(row) => router.push(hrefFor(row.documentId))}
        empty={{
          title: `No ${lower} items`,
          description: `The lines of raised ${lower}s appear here.`,
        }}
      />
    </>
  );
}

function columns(noun: string): ColumnDef<DocumentLineRow, unknown>[] {
  return [
    {
      id: "date",
      accessorFn: (row) => row.date,
      header: "Date",
      cell: ({ row }) => <span className="whitespace-nowrap text-muted">{formatReportDate(row.original.date)}</span>,
    },
    {
      id: "number",
      accessorFn: (row) => row.documentNumber,
      header: `${noun} No`,
      cell: ({ row }) => <span className="whitespace-nowrap font-medium text-text">{row.original.documentNumber}</span>,
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
}
