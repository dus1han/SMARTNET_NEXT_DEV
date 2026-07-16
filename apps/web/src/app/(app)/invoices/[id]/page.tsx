"use client";

/**
 * One invoice, in full — the read view.
 *
 * A client component, so it reads the route param with `useParams` (this Next passes `params` as a
 * promise; the hook unwraps it). It shows the document as it stands, the lines, the totals, and the
 * derived outstanding, plus the History tab — the audit trail and the version snapshots the save
 * pipeline writes, dropped in from Phase 2's reusable component.
 */

import { useQuery } from "@tanstack/react-query";
import { useParams } from "next/navigation";
import Link from "next/link";
import { ArrowLeft } from "lucide-react";
import { ApiError } from "@/lib/api";
import { getInvoice } from "@/lib/invoices";
import { PageHeader } from "@/components/shell/app-shell";
import { DataTable, type ColumnDef } from "@/components/data-table";
import { formatMoney, formatReportDate } from "@/components/reports";
import { Badge, Card, ErrorBanner, FadeIn, Skeleton } from "@/components/ui";
import { History } from "@/components/history/history";
import type { InvoiceLineDetail } from "@/lib/invoices";

export default function InvoiceViewPage() {
  const { id } = useParams<{ id: string }>();
  const invoiceId = Number(id);

  const invoice = useQuery({
    queryKey: ["invoice", invoiceId],
    queryFn: () => getInvoice(invoiceId),
    enabled: Number.isFinite(invoiceId),
  });

  const error = invoice.error as ApiError | null;
  const data = invoice.data;
  const settled = (data?.outstanding ?? 0) <= 0;

  return (
    <FadeIn className="space-y-6">
      <Link
        href="/invoices"
        className="inline-flex items-center gap-1.5 text-sm text-muted transition-colors hover:text-text"
      >
        <ArrowLeft className="size-4" aria-hidden />
        All invoices
      </Link>

      <PageHeader
        title={data ? `Invoice ${data.number}` : "Invoice"}
        description={data ? `${data.type} · ${formatReportDate(data.date)}` : undefined}
      />

      {error && <ErrorBanner message={error.message} correlationId={error.correlationId} />}

      {invoice.isPending && <Skeleton className="h-40" />}

      {data && (
        <>
          <div className="grid gap-4 md:grid-cols-3">
            <Detail label="Customer" value={data.customerName ?? "—"} sub={data.customerCode ?? undefined} />
            <Detail label="PO number" value={data.purchaseOrderNo || "—"} />
            <Detail label="Contact" value={data.contactPerson || "—"} />
          </div>

          <DataTable columns={lineColumns} rows={data.lines} pageSize={50} />

          <div className="grid gap-4 sm:grid-cols-2">
            <Card className="space-y-2 p-5">
              <Row label="Subtotal" value={formatMoney(data.subtotal)} />
              <Row label="Discount" value={`− ${formatMoney(data.discountAmount)}`} />
              <Row label="Net" value={formatMoney(data.netTotal)} />
              <Row label={`VAT (${data.taxRatePercentage}%)`} value={formatMoney(data.taxAmount)} />
              <div className="border-t border-subtle pt-2">
                <Row label="Total" value={formatMoney(data.total)} strong />
              </div>
            </Card>

            <Card className="flex flex-col justify-center gap-2 p-5">
              <p className="stat-label text-xs font-semibold uppercase tracking-wider">Outstanding</p>
              <div className="flex items-center gap-3">
                <span className="tabular text-3xl font-bold text-text">{formatMoney(data.outstanding)}</span>
                <Badge tone={settled ? "success" : "warning"}>{settled ? "Paid" : "Due"}</Badge>
              </div>
              <p className="text-sm text-muted">Derived from the ledger — not a stored figure.</p>
            </Card>
          </div>

          <Card className="p-5">
            <h2 className="mb-4 text-sm font-semibold uppercase tracking-wider text-muted">History</h2>
            <History
              entityType="Invoice"
              entityId={invoiceId}
              document={{ docType: "INVOICE", docId: invoiceId, title: `Invoice ${data.number}` }}
            />
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
    cell: ({ row }) => <span className="tabular text-text">{row.original.quantity}</span>,
  },
  {
    id: "unitPrice",
    accessorFn: (row) => row.unitPrice,
    header: "Unit price",
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
    cell: ({ row }) => <span className="tabular font-medium text-text">{formatMoney(row.original.net)}</span>,
  },
];
