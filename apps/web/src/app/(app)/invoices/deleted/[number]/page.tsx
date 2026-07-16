"use client";

/**
 * One deleted invoice, in full — the read view behind a row of the deleted register.
 *
 * Read-only: the document as it stood when it was voided (header, lines, totals), led by a panel that
 * answers who deleted it, when and why. That panel is shown for a legacy deletion (from del_invoice_h,
 * carrying the old app's deluser/deldate/delreason) exactly as for a new-app void — the accountability
 * is the point of the register, and it must not be a new-documents-only privilege.
 */

import { useQuery } from "@tanstack/react-query";
import { useParams } from "next/navigation";
import Link from "next/link";
import { ArrowLeft, UserRound, CalendarClock, MessageSquareText } from "lucide-react";
import { ApiError } from "@/lib/api";
import { getDeletedInvoice, type InvoiceLineDetail } from "@/lib/invoices";
import { PageHeader } from "@/components/shell/app-shell";
import { DataTable, type ColumnDef } from "@/components/data-table";
import { formatMoney, formatReportDate } from "@/components/reports";
import { Badge, Card, ErrorBanner, FadeIn, Skeleton } from "@/components/ui";

export default function DeletedInvoiceViewPage() {
  const params = useParams<{ number: string }>();
  const number = decodeURIComponent(params.number);

  const deleted = useQuery({
    queryKey: ["deleted-invoice", number],
    queryFn: () => getDeletedInvoice(number),
    enabled: number.length > 0,
  });

  const error = deleted.error as ApiError | null;
  const data = deleted.data;
  const isLegacy = data?.origin === "legacy";

  return (
    <FadeIn className="space-y-6">
      <Link
        href="/invoices/deleted"
        className="inline-flex items-center gap-1.5 text-sm text-muted transition-colors hover:text-text"
      >
        <ArrowLeft className="size-4" aria-hidden />
        Deleted invoices
      </Link>

      <div className="flex flex-wrap items-center gap-3">
        <PageHeader
          title={data ? `Invoice ${data.number}` : "Deleted invoice"}
          description={data ? `${data.kind} invoice · ${data.type} · ${formatReportDate(data.date)}` : undefined}
        />
        <Badge tone="danger">Deleted</Badge>
        {isLegacy && <Badge tone="neutral">Legacy</Badge>}
      </div>

      {error && (
        <ErrorBanner
          message={error.status === 404 ? "This deleted invoice could not be found." : error.message}
          correlationId={error.correlationId}
        />
      )}

      {deleted.isPending && <Skeleton className="h-40" />}

      {data && (
        <>
          {/* Who / when / why — the register's whole reason for existing, shown for legacy and new alike. */}
          <Card className="grid gap-4 p-5 sm:grid-cols-3">
            <Fact icon={<UserRound className="size-4" />} label="Deleted by" value={data.deletedByName || "Unknown"} />
            <Fact icon={<CalendarClock className="size-4" />} label="Deleted on" value={formatReportDate(data.deletedAt)} />
            <Fact icon={<MessageSquareText className="size-4" />} label="Reason" value={data.reason || "—"} />
          </Card>

          <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-4">
            <Detail label="Company" value={data.companyName ?? "—"} />
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
              <p className="stat-label text-xs font-semibold uppercase tracking-wider">Status</p>
              <div className="flex items-center gap-3">
                <span className="text-2xl font-bold text-text">Deleted</span>
                <Badge tone="danger">Voided</Badge>
              </div>
              <p className="text-sm text-muted">
                {isLegacy
                  ? "Recorded in the legacy system's deleted register before cutover — read-only history."
                  : "Voided in this app — a soft delete. Its ledger charge was reversed and any issued stock returned."}
              </p>
            </Card>
          </div>
        </>
      )}
    </FadeIn>
  );
}

function Fact({ icon, label, value }: { icon: React.ReactNode; label: string; value: string }) {
  return (
    <div className="flex items-start gap-3">
      <span className="mt-0.5 text-muted" aria-hidden>
        {icon}
      </span>
      <div className="min-w-0">
        <p className="stat-label text-xs font-semibold uppercase tracking-wider">{label}</p>
        <p className="mt-0.5 break-words font-medium text-text">{value}</p>
      </div>
    </div>
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
