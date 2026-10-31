"use client";

/**
 * One quotation, in full — the read view, with the convert action.
 *
 * A quotation charges nothing, so there is no outstanding figure; what this view adds over the invoice
 * one is conversion. Convert turns the quote into an invoice through the same save pipeline a hand-keyed
 * invoice uses (a real number, a ledger charge, stock issued, a snapshot) and marks the quote converted —
 * once. A converted quote shows a link to the invoice it became instead of the convert button.
 *
 * Or it goes via a proforma invoice: Convert to proforma raises one, and while it is live the quotation is
 * converted, edited and voided through the proforma instead — the server refuses the direct routes, so one
 * quotation can never become two invoices.
 */

import { useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useParams, useRouter } from "next/navigation";
import Link from "next/link";
import { ArrowLeft, ArrowRight, Download, FileCheck2, Mail, Pencil, Printer, Trash2 } from "lucide-react";
import { ApiError } from "@/lib/api";
import { convertQuotation, deleteQuotation, emailQuotation, getQuotation, quotationRecipients } from "@/lib/quotations";
import { createProforma } from "@/lib/proformas";
import { me } from "@/lib/auth";
import { today } from "@/lib/period";
import { PageHeader } from "@/components/shell/app-shell";
import { DataTable, downloadExcel, type ColumnDef } from "@/components/data-table";
import { formatMoney, formatReportDate } from "@/components/reports";
import { Badge, Button, Card, Dialog, ErrorBanner, FadeIn, Input, Skeleton, toast } from "@/components/ui";
import { History } from "@/components/history/history";
import { PrintPreview } from "@/components/print-preview";
import { EmailDocumentDialog } from "@/components/email-document-dialog";
import { ConvertToInvoiceDialog, VoidDocumentDialog } from "@/components/documents/document-dialogs";
import type { InvoiceLineDetail } from "@/lib/invoices";

export default function QuotationViewPage() {
  const { id } = useParams<{ id: string }>();
  const quotationId = Number(id);
  const router = useRouter();
  const queryClient = useQueryClient();

  const quotation = useQuery({
    queryKey: ["quotation", quotationId],
    queryFn: () => getQuotation(quotationId),
    enabled: Number.isFinite(quotationId),
  });

  const user = useQuery({ queryKey: ["me"], queryFn: me });
  const [converting, setConverting] = useState(false);
  const [printing, setPrinting] = useState(false);
  const [emailing, setEmailing] = useState(false);
  const [downloading, setDownloading] = useState(false);
  const [voiding, setVoiding] = useState(false);
  const [raisingProforma, setRaisingProforma] = useState(false);
  const error = quotation.error as ApiError | null;
  const data = quotation.data;
  const converted = data?.convertedInvoiceId != null;
  // A live proforma carries this quote forward: until it is converted or voided, the quote is not converted,
  // edited or voided directly.
  const viaProforma = data?.proformaId != null && !converted;
  const isLegacy = data?.origin === "legacy";
  // A quotation can be edited/voided by someone with the quotation right (a legacy one is adopted on save).
  // A converted quote is spent, so it can only be voided, not edited.
  const canModify = data != null && (user.data?.permissions.includes("item_qu") ?? false);

  return (
    <FadeIn className="space-y-6">
      <Link
        href="/quotations"
        className="inline-flex items-center gap-1.5 text-sm text-muted transition-colors hover:text-text"
      >
        <ArrowLeft className="size-4" aria-hidden />
        All quotations
      </Link>

      <div className="flex flex-wrap items-start justify-between gap-4">
        <div className="flex flex-wrap items-center gap-3">
          <PageHeader
            title={data ? `Quotation ${data.number}` : "Quotation"}
            description={data ? `${data.kind} quotation · ${formatReportDate(data.date)}` : undefined}
          />
          {isLegacy && <Badge tone="neutral">Legacy</Badge>}
        </div>
        <div className="flex flex-wrap items-center gap-2">
          {/* Download, print and email all work on a legacy quotation — the document renders from the
              stored legacy figures, so none of them waits on the quote being adopted. */}
          {data && (
            <Button
              variant="secondary"
              pending={downloading}
              onClick={async () => {
                setDownloading(true);
                try {
                  await downloadExcel(`/api/quotations/${quotationId}/pdf`, `quotation-${data.number}.pdf`);
                  // The download is recorded as a Print event, so History is now stale.
                  void queryClient.invalidateQueries({ queryKey: ["history", "Quotation", String(quotationId)] });
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
          {canModify && !converted && !viaProforma && (
            <Button variant="secondary" onClick={() => router.push(`/quotations/${quotationId}/edit`)}>
              <Pencil />
              Edit
            </Button>
          )}
          {canModify && !viaProforma && (
            <Button variant="secondary" onClick={() => setVoiding(true)}>
              <Trash2 />
              Void
            </Button>
          )}
          {/* Both new and legacy quotes convert — a legacy one is built from its stored lines. */}
          {canModify && !converted && !viaProforma && (
            <Button variant="secondary" onClick={() => setRaisingProforma(true)}>
              <FileCheck2 />
              Convert to proforma
            </Button>
          )}
          {data && !converted && !viaProforma && (
            <Button onClick={() => setConverting(true)}>
              Convert to invoice
              <ArrowRight />
            </Button>
          )}
        </div>
      </div>

      {error && <ErrorBanner message={error.message} correlationId={error.correlationId} />}

      {quotation.isPending && <Skeleton className="h-40" />}

      {data && (
        <>
          {converted && (
            <Card className="flex flex-wrap items-center justify-between gap-3 border-success/40 bg-success-subtle p-4">
              <div>
                <p className="font-medium text-text">Converted to an invoice</p>
                <p className="text-sm text-muted">This quotation has been turned into an invoice and cannot be converted again.</p>
              </div>
              <div className="flex flex-wrap gap-2">
                {data.proformaId != null && (
                  <Button variant="secondary" onClick={() => router.push(`/proforma-invoices/${data.proformaId}`)}>
                    Proforma {data.proformaNumber}
                  </Button>
                )}
                <Button variant="secondary" onClick={() => router.push(`/invoices/${data.convertedInvoiceId}`)}>
                  View invoice {data.convertedInvoiceNumber}
                  <ArrowRight />
                </Button>
              </div>
            </Card>
          )}

          {viaProforma && (
            <Card className="flex flex-wrap items-center justify-between gap-3 border-primary/40 bg-primary/5 p-4">
              <div>
                <p className="font-medium text-text">Proforma invoice {data.proformaNumber} raised</p>
                <p className="text-sm text-muted">
                  Convert the proforma to invoice this quotation. To change the quotation, void the proforma first.
                </p>
              </div>
              <Button variant="secondary" onClick={() => router.push(`/proforma-invoices/${data.proformaId}`)}>
                View proforma {data.proformaNumber}
                <ArrowRight />
              </Button>
            </Card>
          )}

          <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-4">
            <Detail label="Company" value={data.companyName ?? "—"} />
            <Detail label="Customer" value={data.customerName ?? "—"} sub={data.customerCode ?? undefined} />
            <Detail label="Valid for" value={data.validity || "—"} />
            <Detail label="Contact" value={data.contactPerson || "—"} />
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
              entityType="Quotation"
              entityId={quotationId}
              document={{ docType: "QUOTATION", docId: quotationId, title: `Quotation ${data.number}` }}
            />
          </Card>

          <ConvertToInvoiceDialog
            open={converting}
            onOpenChange={setConverting}
            documentLabel={`Quotation ${data.number}`}
            // The server decides this on whether any line still resolves to an item — the same rule the
            // conversion itself applies, so the dialog cannot ask for a cost the conversion ignores, nor
            // omit one it will demand.
            isService={data.kind !== "Item"}
            storedCost={data.cost}
            defaultContact={data.contactPerson}
            onConverted={(invoiceId) => {
              // The quote is now spent; refresh it so a re-open shows the converted banner.
              void queryClient.invalidateQueries({ queryKey: ["quotation", quotationId] });
              router.push(`/invoices/${invoiceId}`);
            }}
            convert={(request) => convertQuotation(quotationId, request)}
          />

          <VoidDocumentDialog
            open={voiding}
            onOpenChange={setVoiding}
            documentLabel={`quotation ${data.number}`}
            description="The quotation is soft-deleted — recoverable and audited. A legacy quotation is adopted into the new system first."
            onVoided={() => {
              void queryClient.invalidateQueries({ queryKey: ["quotations"] });
              toast.success(`Quotation ${data.number} voided.`);
              router.push("/quotations");
            }}
            voidDocument={(reason) => deleteQuotation(quotationId, data.rowVersion, reason)}
          />

          <ProformaDialog
            open={raisingProforma}
            onOpenChange={setRaisingProforma}
            quotationNumber={data.number}
            raise={(date) => createProforma(quotationId, date)}
            onRaised={(proformaId) => {
              void queryClient.invalidateQueries({ queryKey: ["quotation", quotationId] });
              void queryClient.invalidateQueries({ queryKey: ["proformas"] });
              router.push(`/proforma-invoices/${proformaId}`);
            }}
          />

          <PrintPreview
            open={printing}
            onOpenChange={setPrinting}
            path={`/api/quotations/${quotationId}/pdf`}
            title={`Quotation ${data.number}`}
            // Fetching it records a Print event, so the timeline is stale once it loads.
            onLoaded={() => queryClient.invalidateQueries({ queryKey: ["history", "Quotation", String(quotationId)] })}
          />

          <EmailDocumentDialog
            open={emailing}
            onOpenChange={setEmailing}
            documentId={quotationId}
            documentLabel={`Quotation ${data.number}`}
            queryKey="quotation"
            fetchRecipients={quotationRecipients}
            send={(id, contactIds) => emailQuotation(id, { contactIds })}
            onSent={() => queryClient.invalidateQueries({ queryKey: ["history", "Quotation", String(quotationId)] })}
          />
        </>
      )}
    </FadeIn>
  );
}

/**
 * Raise a proforma invoice from this quotation. The only thing asked is its date: the proforma is valued at
 * that date's VAT rate, and everything else is the quotation's.
 */
function ProformaDialog({ open, onOpenChange, quotationNumber, raise, onRaised }: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  quotationNumber: string;
  raise: (date: string) => Promise<{ id: number; number: string }>;
  onRaised: (proformaId: number) => void;
}) {
  const [date, setDate] = useState(today);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  async function submit() {
    setSubmitting(true);
    setError(null);
    try {
      const proforma = await raise(date);
      toast.success(`Proforma invoice ${proforma.number} raised from ${quotationNumber}.`);
      onOpenChange(false);
      onRaised(proforma.id);
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
      title={`Convert ${quotationNumber} to a proforma invoice`}
      description="The proforma carries this quotation's customer, items and prices, and gets its own number. It charges nothing — the customer is billed when the proforma is converted to an invoice."
      footer={
        <>
          <Button variant="secondary" onClick={() => onOpenChange(false)} disabled={submitting}>
            Cancel
          </Button>
          <Button onClick={submit} pending={submitting} disabled={date === ""}>
            Raise proforma
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        {error && <ErrorBanner message={error.message} correlationId={error.correlationId} />}
        <Input label="Proforma date" type="date" value={date} onChange={(e) => setDate(e.target.value)} />
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
