"use client";

/**
 * One credit note, in full — the read view.
 *
 * A credit note reverses part or all of an invoice: it posts a Credit to the ledger (reducing what the
 * customer owes) and, where it returns goods, a stock receipt. This view shows the invoice it credits, the
 * lines credited, and how the note was issued — the reprint. There is no edit or delete here; that is the
 * soft, reason-gated delete of slice 5.
 */

import { useQuery } from "@tanstack/react-query";
import { useParams, useRouter } from "next/navigation";
import Link from "next/link";
import { ArrowLeft, ArrowRight } from "lucide-react";
import { ApiError } from "@/lib/api";
import { getCreditNote } from "@/lib/credit-notes";
import { PageHeader } from "@/components/shell/app-shell";
import { DataTable, type ColumnDef } from "@/components/data-table";
import { formatMoney, formatReportDate } from "@/components/reports";
import { Badge, Button, Card, ErrorBanner, FadeIn, Skeleton } from "@/components/ui";
import { History } from "@/components/history/history";
import type { InvoiceLineDetail } from "@/lib/invoices";

export default function CreditNoteViewPage() {
  const { id } = useParams<{ id: string }>();
  const creditNoteId = Number(id);
  const router = useRouter();

  const creditNote = useQuery({
    queryKey: ["credit-note", creditNoteId],
    queryFn: () => getCreditNote(creditNoteId),
    enabled: Number.isFinite(creditNoteId),
  });

  const error = creditNote.error as ApiError | null;
  const data = creditNote.data;
  const isLegacy = data?.origin === "legacy";

  return (
    <FadeIn className="space-y-6">
      <Link
        href="/credit-notes"
        className="inline-flex items-center gap-1.5 text-sm text-muted transition-colors hover:text-text"
      >
        <ArrowLeft className="size-4" aria-hidden />
        All credit notes
      </Link>

      <div className="flex flex-wrap items-start justify-between gap-4">
        <div className="flex flex-wrap items-center gap-3">
          <PageHeader
            title={data ? `Credit note ${data.number}` : "Credit note"}
            description={data ? `${data.kind} credit note · ${formatReportDate(data.date)}` : undefined}
          />
          {isLegacy && <Badge tone="neutral">Legacy</Badge>}
          {data?.returnsStock && <Badge tone="success">Returns stock</Badge>}
        </div>
        {/* The invoice it credits — new notes carry a surrogate link; a legacy one only its number. */}
        {data?.invoiceId != null && (
          <Button variant="secondary" onClick={() => router.push(`/invoices/${data.invoiceId}`)}>
            View invoice {data.invoiceNumber}
            <ArrowRight />
          </Button>
        )}
      </div>

      {error && <ErrorBanner message={error.message} correlationId={error.correlationId} />}

      {creditNote.isPending && <Skeleton className="h-40" />}

      {data && (
        <>
          <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-4">
            <Detail label="Company" value={data.companyName ?? "—"} />
            <Detail label="Customer" value={data.customerName ?? "—"} sub={data.customerCode ?? undefined} />
            <Detail label="Credits invoice" value={data.invoiceNumber} />
            <Detail label="Returns goods to stock" value={data.returnsStock ? "Yes" : "No"} />
          </div>

          <DataTable columns={lineColumns} rows={data.lines} pageSize={50} />

          <div className="grid gap-4 sm:grid-cols-2">
            <div />
            <Card className="space-y-2 p-5">
              <Row label="Subtotal" value={formatMoney(data.subtotal)} />
              {data.discountAmount > 0 && <Row label="Discount" value={`− ${formatMoney(data.discountAmount)}`} />}
              <Row label="Net" value={formatMoney(data.netTotal)} />
              <Row label={`VAT (${data.taxRatePercentage}%)`} value={formatMoney(data.taxAmount)} />
              <div className="border-t border-subtle pt-2">
                <Row label="Credited" value={formatMoney(data.total)} strong />
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
                entityType="CreditNote"
                entityId={creditNoteId}
                document={{ docType: "CN", docId: creditNoteId, title: `Credit note ${data.number}` }}
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
    id: "net",
    accessorFn: (row) => row.net,
    header: "Net",
    meta: { align: "right" },
    cell: ({ row }) => <span className="tabular font-medium text-text">{formatMoney(row.original.net)}</span>,
  },
];
