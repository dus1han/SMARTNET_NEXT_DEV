"use client";

/**
 * One proforma invoice — the read view, with its place in the chain.
 *
 * Quotation → proforma → invoice. This screen links back to the quotation it was raised from and forward to
 * the invoice it became, prints and emails the proforma, converts it to an invoice (once), and voids it —
 * which frees the quotation to be changed or converted again. A proforma is never edited: its content is the
 * quotation's, so a change is made to the quotation and a new proforma raised.
 */

import { useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useParams, useRouter } from "next/navigation";
import Link from "next/link";
import { ArrowLeft, ArrowRight, Download, Mail, Printer, Trash2 } from "lucide-react";
import { ApiError } from "@/lib/api";
import { me } from "@/lib/auth";
import { convertProforma, emailProforma, getProforma, proformaRecipients, voidProforma } from "@/lib/proformas";
import type { InvoiceLineDetail } from "@/lib/invoices";
import { PageHeader } from "@/components/shell/app-shell";
import { DataTable, downloadExcel, type ColumnDef } from "@/components/data-table";
import { formatMoney, formatReportDate } from "@/components/reports";
import { Button, Card, ErrorBanner, FadeIn, Skeleton, toast } from "@/components/ui";
import { History } from "@/components/history/history";
import { PrintPreview } from "@/components/print-preview";
import { EmailDocumentDialog } from "@/components/email-document-dialog";
import { ConvertToInvoiceDialog, VoidDocumentDialog } from "@/components/documents/document-dialogs";
import { ProformaStatusBadge } from "@/components/documents/proforma-status";

export default function ProformaViewPage() {
  const { id } = useParams<{ id: string }>();
  const proformaId = Number(id);
  const router = useRouter();
  const queryClient = useQueryClient();

  const proforma = useQuery({
    queryKey: ["proforma", proformaId],
    queryFn: () => getProforma(proformaId),
    enabled: Number.isFinite(proformaId),
  });

  const user = useQuery({ queryKey: ["me"], queryFn: me });
  const [converting, setConverting] = useState(false);
  const [voiding, setVoiding] = useState(false);
  const [printing, setPrinting] = useState(false);
  const [emailing, setEmailing] = useState(false);
  const [downloading, setDownloading] = useState(false);

  const error = proforma.error as ApiError | null;
  const data = proforma.data;
  const open = data?.status === "Open";
  const permissions = user.data?.permissions ?? [];
  // Converting raises an invoice, so it takes the invoice right; voiding takes the quotation right.
  const canConvert = open && permissions.includes("item_in");
  const canVoid = open && permissions.includes("item_qu");

  const refreshHistory = () =>
    queryClient.invalidateQueries({ queryKey: ["history", "ProformaInvoice", String(proformaId)] });

  return (
    <FadeIn className="space-y-6">
      <Link
        href="/proforma-invoices"
        className="inline-flex items-center gap-1.5 text-sm text-muted transition-colors hover:text-text"
      >
        <ArrowLeft className="size-4" aria-hidden />
        All proforma invoices
      </Link>

      <div className="flex flex-wrap items-start justify-between gap-4">
        <div className="flex flex-wrap items-center gap-3">
          <PageHeader
            title={data ? `Proforma invoice ${data.number}` : "Proforma invoice"}
            description={data ? `${data.kind} · ${formatReportDate(data.date)}` : undefined}
          />
          {data && <ProformaStatusBadge status={data.status} />}
        </div>
        <div className="flex flex-wrap items-center gap-2">
          {data && (
            <>
              <Button
                variant="secondary"
                pending={downloading}
                onClick={async () => {
                  setDownloading(true);
                  try {
                    await downloadExcel(`/api/proforma-invoices/${proformaId}/pdf`, `proforma-${data.number}.pdf`);
                    void refreshHistory();
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
          {canVoid && (
            <Button variant="secondary" onClick={() => setVoiding(true)}>
              <Trash2 />
              Void
            </Button>
          )}
          {canConvert && (
            <Button onClick={() => setConverting(true)}>
              Convert to invoice
              <ArrowRight />
            </Button>
          )}
        </div>
      </div>

      {error && <ErrorBanner message={error.message} correlationId={error.correlationId} />}

      {proforma.isPending && <Skeleton className="h-40" />}

      {data && (
        <>
          {data.status === "Invoiced" && (
            <Card className="flex flex-wrap items-center justify-between gap-3 border-success/40 bg-success-subtle p-4">
              <div>
                <p className="font-medium text-text">Converted to an invoice</p>
                <p className="text-sm text-muted">This proforma has been invoiced and cannot be converted or voided.</p>
              </div>
              <Button variant="secondary" onClick={() => router.push(`/invoices/${data.convertedInvoiceId}`)}>
                View invoice {data.convertedInvoiceNumber}
                <ArrowRight />
              </Button>
            </Card>
          )}

          {data.status === "Voided" && (
            <Card className="border-danger/40 bg-danger-subtle p-4">
              <p className="font-medium text-text">Voided</p>
              <p className="text-sm text-muted">
                This proforma was voided{data.voidedAt ? ` on ${formatReportDate(data.voidedAt.slice(0, 10))}` : ""} and is
                kept for the record. Its quotation is free to be changed or converted again.
              </p>
            </Card>
          )}

          <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-4">
            <Detail label="Company" value={data.companyName ?? "—"} />
            <Detail label="Customer" value={data.customerName ?? "—"} sub={data.customerCode ?? undefined} />
            <Card className="p-4">
              <p className="stat-label text-xs font-semibold uppercase tracking-wider">Quotation</p>
              <Link href={`/quotations/${data.quotationId}`} className="mt-1 block font-medium text-primary hover:underline">
                {data.quotationNumber}
              </Link>
            </Card>
            <Detail label="Contact" value={data.contactPerson || "—"} sub={data.validity ? `Valid for ${data.validity}` : undefined} />
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
                <Row label="Amount due" value={formatMoney(data.total)} strong />
              </div>
            </Card>
          </div>

          <Card className="p-5">
            <h2 className="mb-4 text-sm font-semibold uppercase tracking-wider text-muted">History</h2>
            <History
              entityType="ProformaInvoice"
              entityId={proformaId}
              document={{ docType: "PROFORMA", docId: proformaId, title: `Proforma invoice ${data.number}` }}
            />
          </Card>

          <ConvertToInvoiceDialog
            open={converting}
            onOpenChange={setConverting}
            documentLabel={`Proforma invoice ${data.number}`}
            isService={data.kind !== "Item"}
            defaultContact={data.contactPerson}
            convert={(request) => convertProforma(proformaId, request)}
            onConverted={(invoiceId) => {
              void queryClient.invalidateQueries({ queryKey: ["proforma", proformaId] });
              void queryClient.invalidateQueries({ queryKey: ["proformas"] });
              void queryClient.invalidateQueries({ queryKey: ["quotation", data.quotationId] });
              router.push(`/invoices/${invoiceId}`);
            }}
          />

          <VoidDocumentDialog
            open={voiding}
            onOpenChange={setVoiding}
            documentLabel={`proforma invoice ${data.number}`}
            description="The proforma is kept on record, marked voided. Its quotation becomes free to edit, convert to a new proforma, or invoice directly."
            voidDocument={(reason) => voidProforma(proformaId, data.rowVersion, reason)}
            onVoided={() => {
              void queryClient.invalidateQueries({ queryKey: ["proformas"] });
              void queryClient.invalidateQueries({ queryKey: ["proforma", proformaId] });
              void queryClient.invalidateQueries({ queryKey: ["quotation", data.quotationId] });
              toast.success(`Proforma invoice ${data.number} voided.`);
            }}
          />

          <PrintPreview
            open={printing}
            onOpenChange={setPrinting}
            path={`/api/proforma-invoices/${proformaId}/pdf`}
            title={`Proforma invoice ${data.number}`}
            onLoaded={() => void refreshHistory()}
          />

          <EmailDocumentDialog
            open={emailing}
            onOpenChange={setEmailing}
            documentId={proformaId}
            documentLabel={`Proforma invoice ${data.number}`}
            queryKey="proforma"
            fetchRecipients={proformaRecipients}
            send={(docId, contactIds) => emailProforma(docId, { contactIds })}
            onSent={() => void refreshHistory()}
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
