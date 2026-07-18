"use client";

/**
 * One credit note, in full — the read view.
 *
 * A credit note reverses part or all of an invoice: it posts a Credit to the ledger (reducing what the
 * customer owes) and, where it returns goods, a stock receipt. This view shows the invoice it credits, the
 * lines credited, and how the note was issued — the reprint. There is no edit or delete here; that is the
 * soft, reason-gated delete of slice 5.
 */

import { useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useParams, useRouter } from "next/navigation";
import Link from "next/link";
import { ArrowLeft, ArrowRight, Download, Mail, Printer, Trash2 } from "lucide-react";
import { ApiError } from "@/lib/api";
import { creditNoteRecipients, deleteCreditNote, emailCreditNote, getCreditNote } from "@/lib/credit-notes";
import { me } from "@/lib/auth";
import { PageHeader } from "@/components/shell/app-shell";
import { DataTable, downloadExcel, type ColumnDef } from "@/components/data-table";
import { formatMoney, formatReportDate } from "@/components/reports";
import { Badge, Button, Card, Dialog, ErrorBanner, FadeIn, Input, Skeleton, toast } from "@/components/ui";
import { History } from "@/components/history/history";
import { PrintPreview } from "@/components/print-preview";
import { EmailDocumentDialog } from "@/components/email-document-dialog";
import type { InvoiceLineDetail } from "@/lib/invoices";

export default function CreditNoteViewPage() {
  const { id } = useParams<{ id: string }>();
  const creditNoteId = Number(id);
  const router = useRouter();
  const queryClient = useQueryClient();

  const [printing, setPrinting] = useState(false);
  const [emailing, setEmailing] = useState(false);
  const [downloading, setDownloading] = useState(false);
  const [voiding, setVoiding] = useState(false);

  const creditNote = useQuery({
    queryKey: ["credit-note", creditNoteId],
    queryFn: () => getCreditNote(creditNoteId),
    enabled: Number.isFinite(creditNoteId),
  });
  const user = useQuery({ queryKey: ["me"], queryFn: me });

  const error = creditNote.error as ApiError | null;
  const data = creditNote.data;
  const isLegacy = data?.origin === "legacy";

  // Voiding a credit note moves money — it is gated on the same permission that raises one. Hiding the
  // button is a courtesy; the endpoint re-checks.
  const canVoid = data != null && (user.data?.permissions.includes("new_cn") ?? false);

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
        <div className="flex flex-wrap items-center gap-2">
          {/* Download, print and email all work on a legacy note — the document renders from the
              stored legacy figures, so none of them waits on the note being adopted. */}
          {data && (
            <Button
              variant="secondary"
              pending={downloading}
              onClick={async () => {
                setDownloading(true);
                try {
                  await downloadExcel(`/api/credit-notes/${creditNoteId}/pdf`, `credit-note-${data.number}.pdf`);
                  // The download is recorded as a Print event, so History is now stale.
                  void queryClient.invalidateQueries({ queryKey: ["history", "CreditNote", String(creditNoteId)] });
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
          {canVoid && (
            <Button variant="secondary" onClick={() => setVoiding(true)}>
              <Trash2 />
              Void
            </Button>
          )}
          {/* The invoice it credits — new notes carry a surrogate link; a legacy one only its number. */}
          {data?.invoiceId != null && (
            <Button variant="secondary" onClick={() => router.push(`/invoices/${data.invoiceId}`)}>
              View invoice {data.invoiceNumber}
              <ArrowRight />
            </Button>
          )}
        </div>
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
            {isLegacy && (
              <p className="mb-3 text-sm text-muted">
                Imported from the legacy system — anything before the migration lives in the old app.
              </p>
            )}
            <History
              entityType="CreditNote"
              entityId={creditNoteId}
              document={{ docType: "CN", docId: creditNoteId, title: `Credit note ${data.number}` }}
            />
          </Card>

          <PrintPreview
            open={printing}
            onOpenChange={setPrinting}
            path={`/api/credit-notes/${creditNoteId}/pdf`}
            title={`Credit note ${data.number}`}
            // Fetching it records a Print event, so the timeline is stale once it loads.
            onLoaded={() => queryClient.invalidateQueries({ queryKey: ["history", "CreditNote", String(creditNoteId)] })}
          />

          <EmailDocumentDialog
            open={emailing}
            onOpenChange={setEmailing}
            documentId={creditNoteId}
            documentLabel={`Credit note ${data.number}`}
            queryKey="credit-note"
            fetchRecipients={creditNoteRecipients}
            send={(noteId, contactIds) => emailCreditNote(noteId, { contactIds })}
            onSent={() => queryClient.invalidateQueries({ queryKey: ["history", "CreditNote", String(creditNoteId)] })}
          />

          <VoidDialog
            open={voiding}
            onOpenChange={setVoiding}
            creditNoteNumber={data.number}
            onVoided={() => {
              void queryClient.invalidateQueries({ queryKey: ["credit-notes"] });
              toast.success(`Credit note ${data.number} voided.`);
              router.push("/credit-notes");
            }}
            voidNote={(reason) => deleteCreditNote(creditNoteId, data.rowVersion, reason)}
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
    id: "net",
    accessorFn: (row) => row.net,
    header: "Net",
    meta: { align: "right" },
    cell: ({ row }) => <span className="tabular font-medium text-text">{formatMoney(row.original.net)}</span>,
  },
];

/**
 * Voids a credit note, with the reason the audit trail requires.
 *
 * There is no edit counterpart on purpose: a credit note exists to reverse an invoice, and a correction
 * to one already sent is a new note rather than a rewrite of the old.
 */
function VoidDialog({ open, onOpenChange, creditNoteNumber, onVoided, voidNote }: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  creditNoteNumber: string;
  onVoided: () => void;
  voidNote: (reason: string) => Promise<unknown>;
}) {
  const [reason, setReason] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  async function submit() {
    setSubmitting(true);
    setError(null);
    try {
      await voidNote(reason);
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
      title={`Void credit note ${creditNoteNumber}`}
      description="The note is soft-deleted — recoverable and audited. The credit it posted is reversed, and any stock it returned is issued out again."
      footer={
        <>
          <Button variant="secondary" onClick={() => onOpenChange(false)} disabled={submitting}>
            Cancel
          </Button>
          <Button onClick={submit} pending={submitting} disabled={reason.trim().length < 10}>
            Void credit note
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
          placeholder="Why is this credit note being voided?"
        />
      </div>
    </Dialog>
  );
}
