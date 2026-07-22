"use client";

/**
 * The purchase-order list.
 *
 * The POs this app has raised and the legacy ones adopted from the old system, newest first. A PO is an
 * order — it charges nothing and issues nothing — so there is no outstanding or status column; the goods
 * receipt (the deferred GRN) and the payable (the supplier invoice) are separate documents.
 *
 * Behind the Drafts tab is the other half: orders that have been typed but not raised. They are kept
 * apart rather than mixed in, because a draft has no number and no place in any report.
 */

import { useQuery } from "@tanstack/react-query";
import { useRouter } from "next/navigation";
import { useState } from "react";
import { Plus } from "lucide-react";
import { ApiError } from "@/lib/api";
import { DRAFT_PURCHASE_ORDER } from "@/lib/drafts";
import { getPurchaseOrders, type PurchaseOrderSummary } from "@/lib/purchase-orders";
import { PageHeader } from "@/components/shell/app-shell";
import { DataTable, type ColumnDef } from "@/components/data-table";
import { DocumentViewFilter, DraftsPanel, type DocumentView } from "@/components/documents/drafts-panel";
import { formatMoney, formatReportDate } from "@/components/reports";
import { Badge, Button, ErrorBanner, FadeIn } from "@/components/ui";

export default function PurchaseOrdersPage() {
  const router = useRouter();
  const [view, setView] = useState<DocumentView>("issued");
  const orders = useQuery({ queryKey: ["purchase-orders"], queryFn: getPurchaseOrders });
  const error = orders.error as ApiError | null;

  return (
    <FadeIn className="space-y-6">
      <PageHeader
        title="Purchase orders"
        description="Every purchase order raised in the new system and adopted from the old one, newest first. A PO is an order — goods are received and paid for separately."
      />

      <DocumentViewFilter
        view={view}
        onChange={setView}
        docType={DRAFT_PURCHASE_ORDER}
        issuedLabel="Purchase orders"
      />

      {view === "drafts" ? (
        <DraftsPanel
          docType={DRAFT_PURCHASE_ORDER}
          resumeHref="/purchase-orders/new"
          noun="purchase order"
          partyLabel="Supplier"
        />
      ) : (
        <>
          {error && <ErrorBanner message={error.message} correlationId={error.correlationId} />}

          <DataTable
            columns={columns}
            rows={orders.data}
            loading={orders.isPending}
            searchable={(row) => `${row.number} ${row.supplierName ?? ""}`}
            searchPlaceholder="Search by number or supplier…"
            defaultSort={{ id: "date", desc: true }}
            actions={
              <Button size="sm" onClick={() => router.push("/purchase-orders/new")}>
                <Plus />
                New purchase order
              </Button>
            }
            onRowClick={(row) => router.push(`/purchase-orders/${row.id}`)}
            empty={{
              title: "No purchase orders yet",
              description: "Purchase orders raised in the new system appear here.",
            }}
          />
        </>
      )}
    </FadeIn>
  );
}

const columns: ColumnDef<PurchaseOrderSummary, unknown>[] = [
  // Date leads and the list opens newest-first: a purchase order is looked for by when it was raised
  // far more often than by its number.
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
    id: "supplier",
    accessorFn: (row) => row.supplierName ?? "",
    header: "Supplier",
    cell: ({ row }) => <span className="text-text">{row.original.supplierName ?? "—"}</span>,
  },
  {
    id: "total",
    accessorFn: (row) => row.total,
    header: "Total",
    meta: { align: "right" },
    // Right-aligned like the invoices list — it is a money value in the same column band.
    cell: ({ row }) => <span className="tabular font-medium text-text">{formatMoney(row.original.total)}</span>,
  },
];
