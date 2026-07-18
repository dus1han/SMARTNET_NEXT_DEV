"use client";

/**
 * One purchase order, in full — the read view (Phase 6 slice 1).
 *
 * A PO charges nothing and issues nothing, so there is no outstanding figure and no status: goods arrive
 * on a separate goods receipt (the deferred GRN) and the payable is the supplier invoice. This view shows
 * the order as it was raised, its lines and totals, and — for a new PO — its version history.
 */

import { useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useParams, useRouter } from "next/navigation";
import Link from "next/link";
import { ArrowLeft, Download, Mail, Pencil, Printer, Trash2 } from "lucide-react";
import { ApiError } from "@/lib/api";
import {
  deletePurchaseOrder,
  emailPurchaseOrder,
  getPurchaseOrder,
  purchaseOrderRecipients,
} from "@/lib/purchase-orders";
import { me } from "@/lib/auth";
import { PageHeader } from "@/components/shell/app-shell";
import { DataTable, downloadExcel, type ColumnDef } from "@/components/data-table";
import { formatMoney, formatReportDate } from "@/components/reports";
import { Badge, Button, Card, Dialog, ErrorBanner, FadeIn, Input, Skeleton, toast } from "@/components/ui";
import { History } from "@/components/history/history";
import { PrintPreview } from "@/components/print-preview";
import { EmailDocumentDialog } from "@/components/email-document-dialog";
import type { InvoiceLineDetail } from "@/lib/invoices";

export default function PurchaseOrderViewPage() {
  const { id } = useParams<{ id: string }>();
  const orderId = Number(id);
  const queryClient = useQueryClient();
  const router = useRouter();

  const [printing, setPrinting] = useState(false);
  const [emailing, setEmailing] = useState(false);
  const [downloading, setDownloading] = useState(false);
  const [voiding, setVoiding] = useState(false);

  const user = useQuery({ queryKey: ["me"], queryFn: me });

  const order = useQuery({
    queryKey: ["purchase-order", orderId],
    queryFn: () => getPurchaseOrder(orderId),
    enabled: Number.isFinite(orderId),
  });

  const error = order.error as ApiError | null;
  const data = order.data;
  const isLegacy = data?.origin === "legacy";

  // Editing and voiding an order are gated on the permission that raises one. Hiding the buttons is a
  // courtesy; the endpoints re-check.
  const canModify = data != null && (user.data?.permissions.includes("purchaseorder") ?? false);

  return (
    <FadeIn className="space-y-6">
      <Link
        href="/purchase-orders"
        className="inline-flex items-center gap-1.5 text-sm text-muted transition-colors hover:text-text"
      >
        <ArrowLeft className="size-4" aria-hidden />
        All purchase orders
      </Link>

      <div className="flex flex-wrap items-start justify-between gap-4">
        <div className="flex flex-wrap items-center gap-3">
          <PageHeader
            title={data ? `Purchase order ${data.number}` : "Purchase order"}
            description={data ? `${data.kind} order · ${formatReportDate(data.date)}` : undefined}
          />
          {isLegacy && <Badge tone="neutral">Legacy</Badge>}
        </div>
        <div className="flex flex-wrap items-center gap-2">
          {/* Download, print and email all work on a legacy order — the document renders from the
              stored legacy figures, so none of them waits on the order being adopted. */}
          {data && (
            <Button
              variant="secondary"
              pending={downloading}
              onClick={async () => {
                setDownloading(true);
                try {
                  await downloadExcel(`/api/purchase-orders/${orderId}/pdf`, `purchase-order-${data.number}.pdf`);
                  // The download is recorded as a Print event, so History is now stale.
                  void queryClient.invalidateQueries({ queryKey: ["history", "PurchaseOrder", String(orderId)] });
                } catch {
                  toast.error("The download failed.");
                } finally {
                  setDownloading(false);
                }
              }}
            >
              <Download />
              Download PDF
            </Button>
          )}
          {data && (
            <Button variant="secondary" onClick={() => setPrinting(true)}>
              <Printer />
              Print
            </Button>
          )}
          {data && (
            <Button variant="secondary" onClick={() => setEmailing(true)}>
              <Mail />
              Email
            </Button>
          )}
          {canModify && (
            <Button variant="secondary" onClick={() => router.push(`/purchase-orders/${orderId}/edit`)}>
              <Pencil />
              Edit
            </Button>
          )}
          {canModify && (
            <Button variant="secondary" onClick={() => setVoiding(true)}>
              <Trash2 />
              Void
            </Button>
          )}
        </div>
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
            {isLegacy && (
              <p className="mb-3 text-sm text-muted">
                Imported from the legacy system — anything before the migration lives in the old app.
              </p>
            )}
            <History
              entityType="PurchaseOrder"
              entityId={orderId}
              document={{ docType: "PO", docId: orderId, title: `Purchase order ${data.number}` }}
            />
          </Card>

          <PrintPreview
            open={printing}
            onOpenChange={setPrinting}
            path={`/api/purchase-orders/${orderId}/pdf`}
            title={`Purchase order ${data.number}`}
            // Fetching it records a Print event, so the timeline is stale once it loads.
            onLoaded={() => queryClient.invalidateQueries({ queryKey: ["history", "PurchaseOrder", String(orderId)] })}
          />

          <EmailDocumentDialog
            open={emailing}
            onOpenChange={setEmailing}
            documentId={orderId}
            documentLabel={`Purchase order ${data.number}`}
            queryKey="purchase-order"
            fetchRecipients={purchaseOrderRecipients}
            send={(id, contactIds) => emailPurchaseOrder(id, { contactIds })}
            onSent={() => queryClient.invalidateQueries({ queryKey: ["history", "PurchaseOrder", String(orderId)] })}
          />

          <VoidDialog
            open={voiding}
            onOpenChange={setVoiding}
            orderNumber={data.number}
            onVoided={() => {
              void queryClient.invalidateQueries({ queryKey: ["purchase-orders"] });
              toast.success(`Purchase order ${data.number} voided.`);
              router.push("/purchase-orders");
            }}
            voidOrder={(reason) => deletePurchaseOrder(orderId, data.rowVersion, reason)}
          />
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

/** Voids a purchase order, with the reason the audit trail requires. */
function VoidDialog({ open, onOpenChange, orderNumber, onVoided, voidOrder }: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  orderNumber: string;
  onVoided: () => void;
  voidOrder: (reason: string) => Promise<unknown>;
}) {
  const [reason, setReason] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  async function submit() {
    setSubmitting(true);
    setError(null);
    try {
      await voidOrder(reason);
      onOpenChange(false);
      onVoided();
    } catch (e) {
      setError(e as ApiError);
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <Dialog
      open={open}
      onOpenChange={onOpenChange}
      title={`Void purchase order ${orderNumber}`}
      description="The order is soft-deleted — recoverable and audited. An order posts no ledger entry and no stock movement, so there is nothing to reverse."
      footer={
        <>
          <Button variant="secondary" onClick={() => onOpenChange(false)} disabled={submitting}>
            Cancel
          </Button>
          <Button onClick={submit} pending={submitting} disabled={reason.trim().length < 10}>
            Void purchase order
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        {error && <ErrorBanner message={error.message} correlationId={error.correlationId} />}
        <Input
          label="Reason"
          value={reason}
          onChange={(e) => setReason(e.target.value)}
          hint="At least 10 characters — this is recorded on the audit trail."
          placeholder="Why is this order being voided?"
        />
      </div>
    </Dialog>
  );
}
