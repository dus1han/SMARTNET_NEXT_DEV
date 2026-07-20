"use client";

/**
 * One invoice, in full — the read view.
 *
 * A client component, so it reads the route param with `useParams` (this Next passes `params` as a
 * promise; the hook unwraps it). It shows the document as it stands, the lines, the totals, and the
 * derived outstanding, plus the History tab — the audit trail and the version snapshots the save
 * pipeline writes, dropped in from Phase 2's reusable component.
 */

import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useParams, useRouter } from "next/navigation";
import Link from "next/link";
import { useState } from "react";
import { ArrowLeft, Download, Mail, Pencil, Printer, Trash2 } from "lucide-react";
import { ApiError } from "@/lib/api";
import { deleteInvoice, emailInvoice, getInvoice, invoiceRecipients } from "@/lib/invoices";
import { me } from "@/lib/auth";
import { daysDueLabel } from "@/lib/period";
import { PageHeader } from "@/components/shell/app-shell";
import { DataTable, downloadExcel, type ColumnDef } from "@/components/data-table";
import { formatMoney, formatReportDate } from "@/components/reports";
import { Badge, Button, Card, Dialog, ErrorBanner, FadeIn, Input, Skeleton, toast } from "@/components/ui";
import { History } from "@/components/history/history";
import { PrintPreview } from "@/components/print-preview";
import { EmailDocumentDialog } from "@/components/email-document-dialog";
import type { InvoiceLineDetail, InvoicePaymentLine } from "@/lib/invoices";

export default function InvoiceViewPage() {
  const { id } = useParams<{ id: string }>();
  const invoiceId = Number(id);
  const router = useRouter();
  const queryClient = useQueryClient();
  const [voiding, setVoiding] = useState(false);
  const [printing, setPrinting] = useState(false);
  const [emailing, setEmailing] = useState(false);
  const [downloading, setDownloading] = useState(false);

  const invoice = useQuery({
    queryKey: ["invoice", invoiceId],
    queryFn: () => getInvoice(invoiceId),
    enabled: Number.isFinite(invoiceId),
  });
  const user = useQuery({ queryKey: ["me"], queryFn: me });

  const error = invoice.error as ApiError | null;
  const data = invoice.data;
  const settled = (data?.outstanding ?? 0) <= 0;
  const isLegacy = data?.origin === "legacy";
  // Any invoice can be edited or voided by someone with the invoice right — a legacy one is adopted into
  // the new model on the first change. The server re-checks; this only decides whether to draw the buttons.
  const canModify = data != null && (user.data?.permissions.includes("item_in") ?? false);

  return (
    <FadeIn className="space-y-6">
      <Link
        href="/invoices"
        className="inline-flex items-center gap-1.5 text-sm text-muted transition-colors hover:text-text"
      >
        <ArrowLeft className="size-4" aria-hidden />
        All invoices
      </Link>

      <div className="flex flex-wrap items-start justify-between gap-4">
        <div className="flex flex-wrap items-center gap-3">
          <PageHeader
            title={data ? `Invoice ${data.number}` : "Invoice"}
            description={data ? `${data.kind} invoice · ${data.type} · ${formatReportDate(data.date)}` : undefined}
          />
          {isLegacy && <Badge tone="neutral">Legacy</Badge>}
        </div>
        <div className="flex flex-wrap items-center gap-2">
          {/* Download, print and email all work on a legacy invoice — the document renders from the
              stored legacy figures, so none of them waits on the invoice being adopted. A VAT-registered
              company gets the tax invoice, a non-registered one the plain invoice; the renderer chooses. */}
          {data && (
            <>
              <Button
                variant="secondary"
                pending={downloading}
                onClick={async () => {
                  setDownloading(true);
                  try {
                    await downloadExcel(`/api/invoices/${invoiceId}/pdf`, `invoice-${data.number}.pdf`);
                    // The download is recorded as a Print event, so History is now stale.
                    void queryClient.invalidateQueries({ queryKey: ["history", "Invoice", String(invoiceId)] });
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
              <Button variant="secondary" onClick={() => setPrinting(true)}>
                <Printer />
                Print
              </Button>
              <Button variant="secondary" onClick={() => setEmailing(true)}>
                <Mail />
                Email
              </Button>
            </>
          )}
          {canModify && (
            <>
              <Button variant="secondary" onClick={() => router.push(`/invoices/${invoiceId}/edit`)}>
                <Pencil />
                Edit
              </Button>
              <Button variant="secondary" onClick={() => setVoiding(true)}>
                <Trash2 />
                Void
              </Button>
            </>
          )}
        </div>
      </div>

      {error && <ErrorBanner message={error.message} correlationId={error.correlationId} />}

      {invoice.isPending && <Skeleton className="h-40" />}

      {data && (
        <>
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
              <p className="stat-label text-xs font-semibold uppercase tracking-wider">Outstanding</p>
              <div className="flex items-center gap-3">
                <span className="tabular text-3xl font-bold text-text">{formatMoney(data.outstanding)}</span>
                <Badge tone={settled ? "success" : "warning"}>{settled ? "Paid" : daysDueLabel(data.date)}</Badge>
              </div>
              <p className="text-sm text-muted">
                {isLegacy ? "The legacy system's stored balance, as imported." : "Derived from the ledger — not a stored figure."}
              </p>
            </Card>
          </div>

          {/*
            The detail behind the Outstanding tile. That figure is derived, so without this the screen
            asserts a balance and shows nothing of how it got there — and "why does this say 7,580?"
            is the question the counter actually gets asked.
          */}
          <Card className="p-5">
            <h2 className="mb-4 text-sm font-semibold uppercase tracking-wider text-muted">Payments</h2>
            {data.payments.length === 0 ? (
              <p className="text-sm text-muted">
                {settled
                  ? "Settled, with no payment recorded against it."
                  : "No payments received yet."}
              </p>
            ) : (
              <DataTable columns={paymentColumns} rows={data.payments} pageSize={50} />
            )}
          </Card>

          <Card className="p-5">
            <h2 className="mb-4 text-sm font-semibold uppercase tracking-wider text-muted">History</h2>
            {/*
              Shown for legacy records too. A legacy invoice predates the new app so it carries no
              creation trail or version snapshots — but everything that happens to it *from now on* is
              recorded like any other document, and that is the part staff need to see.
            */}
            {isLegacy && (
              <p className="mb-3 text-sm text-muted">
                Imported from the legacy system — anything before the migration lives in the old app.
              </p>
            )}
            <History
              entityType="Invoice"
              entityId={invoiceId}
              document={{ docType: "INVOICE", docId: invoiceId, title: `Invoice ${data.number}` }}
            />
          </Card>

          <VoidDialog
            open={voiding}
            onOpenChange={setVoiding}
            invoiceNumber={data.number}
            rowVersion={data.rowVersion}
            onVoided={() => {
              void queryClient.invalidateQueries({ queryKey: ["invoices"] });
              toast.success(`Invoice ${data.number} voided.`);
              router.push("/invoices");
            }}
            voidInvoice={(reason) => deleteInvoice(invoiceId, data.rowVersion, reason)}
          />

          <PrintPreview
            open={printing}
            onOpenChange={setPrinting}
            path={`/api/invoices/${invoiceId}/pdf`}
            title={`Invoice ${data.number}`}
            // Fetching it records a Print event, so the timeline is stale once it loads.
            onLoaded={() => queryClient.invalidateQueries({ queryKey: ["history", "Invoice", String(invoiceId)] })}
          />

          <EmailDocumentDialog
            open={emailing}
            onOpenChange={setEmailing}
            documentId={invoiceId}
            documentLabel={`Invoice ${data.number}`}
            queryKey="invoice"
            fetchRecipients={invoiceRecipients}
            send={(id, contactIds) => emailInvoice(id, { contactIds })}
            onSent={() => queryClient.invalidateQueries({ queryKey: ["history", "Invoice", String(invoiceId)] })}
          />
        </>
      )}
    </FadeIn>
  );
}

function VoidDialog({ open, onOpenChange, invoiceNumber, onVoided, voidInvoice }: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  invoiceNumber: string;
  rowVersion: number;
  onVoided: () => void;
  voidInvoice: (reason: string) => Promise<unknown>;
}) {
  const [reason, setReason] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  async function submit() {
    setSubmitting(true);
    setError(null);
    try {
      await voidInvoice(reason);
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
      title={`Void invoice ${invoiceNumber}`}
      description="The invoice is soft-deleted — recoverable and audited. Its ledger charge is reversed and any issued stock returned, through new entries. This cannot be done to a paid invoice."
      footer={
        <>
          <Button variant="secondary" onClick={() => onOpenChange(false)} disabled={submitting}>
            Cancel
          </Button>
          {/* A reason is mandatory, min 10 chars — the server enforces it too. */}
          <Button onClick={submit} pending={submitting} disabled={reason.trim().length < 10}>
            Void invoice
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
          placeholder="Why is this invoice being voided?"
        />
      </div>
    </Dialog>
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

/**
 * A received payment. Mirrors the supplier-invoice table deliberately — the two screens answer the
 * same question from opposite sides of the ledger, and staff move between them.
 */
const paymentColumns: ColumnDef<InvoicePaymentLine, unknown>[] = [
  {
    id: "date",
    accessorFn: (row) => row.date,
    header: "Date",
    cell: ({ row }) => (
      <span className="whitespace-nowrap text-muted">{formatReportDate(row.original.date)}</span>
    ),
  },
  {
    id: "amount",
    accessorFn: (row) => row.amount,
    header: "Amount",
    meta: { align: "right" },
    cell: ({ row }) => (
      <span className="tabular font-medium text-text">{formatMoney(row.original.amount)}</span>
    ),
  },
  {
    id: "method",
    accessorFn: (row) => row.method ?? "",
    header: "Method",
    cell: ({ row }) => <span className="text-text">{row.original.method || "—"}</span>,
  },
  {
    id: "reference",
    accessorFn: (row) => row.reference ?? "",
    header: "Reference",
    cell: ({ row }) => <span className="text-muted">{row.original.reference || "—"}</span>,
  },
];
