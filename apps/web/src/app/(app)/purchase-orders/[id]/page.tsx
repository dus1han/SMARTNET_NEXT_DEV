"use client";

/**
 * One purchase order, in full — the read view (Phase 6 slice 1).
 *
 * A PO charges nothing and issues nothing, so there is no outstanding figure and no status: goods arrive
 * on a separate goods receipt (the deferred GRN) and the payable is the supplier invoice. This view shows
 * the order as it was raised, its lines and totals, and — for a new PO — its version history.
 */

import { useQuery } from "@tanstack/react-query";
import { useParams } from "next/navigation";
import Link from "next/link";
import { ArrowLeft } from "lucide-react";
import { ApiError } from "@/lib/api";
import { getPurchaseOrder } from "@/lib/purchase-orders";
import { PageHeader } from "@/components/shell/app-shell";
import { DataTable, type ColumnDef } from "@/components/data-table";
import { formatMoney, formatReportDate } from "@/components/reports";
import { Badge, Card, ErrorBanner, FadeIn, Skeleton } from "@/components/ui";
import { History } from "@/components/history/history";
import type { InvoiceLineDetail } from "@/lib/invoices";

export default function PurchaseOrderViewPage() {
  const { id } = useParams<{ id: string }>();
  const orderId = Number(id);

  const order = useQuery({
    queryKey: ["purchase-order", orderId],
    queryFn: () => getPurchaseOrder(orderId),
    enabled: Number.isFinite(orderId),
  });

  const error = order.error as ApiError | null;
  const data = order.data;
  const isLegacy = data?.origin === "legacy";

  return (
    <FadeIn className="space-y-6">
      <Link
        href="/purchase-orders"
        className="inline-flex items-center gap-1.5 text-sm text-muted transition-colors hover:text-text"
      >
        <ArrowLeft className="size-4" aria-hidden />
        All purchase orders
      </Link>

      <div className="flex flex-wrap items-center gap-3">
        <PageHeader
          title={data ? `Purchase order ${data.number}` : "Purchase order"}
          description={data ? `${data.kind} order · ${formatReportDate(data.date)}` : undefined}
        />
        {isLegacy && <Badge tone="neutral">Legacy</Badge>}
      </div>

      {error && <ErrorBanner message={error.message} correlationId={error.correlationId} />}

      {order.isPending && <Skeleton className="h-40" />}

      {data && (
        <>
          <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-3">
            <Detail label="Company" value={data.companyName ?? "—"} />
            <Detail label="Supplier" value={data.supplierName ?? "—"} sub={data.supplierCode ?? undefined} />
            <Detail label="Kind" value={data.kind} />
          </div>

          <DataTable columns={lineColumns} rows={data.lines} pageSize={50} />

          <div className="grid gap-4 sm:grid-cols-2">
            <div />
            <Card className="space-y-2 p-5">
              <Row label="Subtotal" value={formatMoney(data.subtotal)} />
              <Row label="Discount" value={`− ${formatMoney(data.discountAmount)}`} />
              <Row label="Net" value={formatMoney(data.netTotal)} />
              <Row label={`VAT (${data.taxRatePercentage}%)`} value={formatMoney(data.taxAmount)} />
              <div className="border-t border-subtle pt-2">
                <Row label="Total" value={formatMoney(data.total)} strong />
              </div>
            </Card>
          </div>

          <Card className="p-5">
            <h2 className="mb-4 text-sm font-semibold uppercase tracking-wider text-muted">History</h2>
            {isLegacy ? (
              <p className="text-sm text-muted">
                Imported from the legacy system — it has no change history in the new app.
              </p>
            ) : (
              <History
                entityType="PurchaseOrder"
                entityId={orderId}
                document={{ docType: "PO", docId: orderId, title: `Purchase order ${data.number}` }}
              />
            )}
          </Card>
        </>
      )}
    </FadeIn>
  );
}

function Detail({ label, value, sub }: { label: string; value: string; sub?: string }) {
  return (
    <Card className="p-4">
      <p className="stat-label text-xs font-semibold uppercase tracking-wider">{label}</p>
      <p className="mt-1 font-medium text-text">{value}</p>
      {sub && <p className="text-sm text-muted">{sub}</p>}
    </Card>
  );
}

function Row({ label, value, strong = false }: { label: string; value: string; strong?: boolean }) {
  return (
    <div className="flex items-center justify-between">
      <span className={strong ? "font-semibold text-text" : "text-muted"}>{label}</span>
      <span className={`tabular ${strong ? "text-lg font-bold text-text" : "text-text"}`}>{value}</span>
    </div>
  );
}

const lineColumns: ColumnDef<InvoiceLineDetail, unknown>[] = [
  {
    id: "description",
    accessorFn: (row) => row.description ?? "",
    header: "Description",
    cell: ({ row }) => (
      <span className="text-text">
        {row.original.description || "—"}
        {row.original.itemCode && <span className="ml-2 text-xs text-muted">{row.original.itemCode}</span>}
      </span>
    ),
  },
  {
    id: "quantity",
    accessorFn: (row) => row.quantity,
    header: "Qty",
    meta: { align: "center" },
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
    id: "discountPercent",
    accessorFn: (row) => row.discountPercent,
    header: "Disc %",
    cell: ({ row }) => <span className="tabular text-muted">{row.original.discountPercent}%</span>,
  },
  {
    id: "net",
    accessorFn: (row) => row.net,
    header: "Net",
    meta: { align: "right" },
    cell: ({ row }) => <span className="tabular font-medium text-text">{formatMoney(row.original.net)}</span>,
  },
];
