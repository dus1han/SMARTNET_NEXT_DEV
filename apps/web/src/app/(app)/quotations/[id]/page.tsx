"use client";

/**
 * One quotation, in full — the read view, with the convert action.
 *
 * A quotation charges nothing, so there is no outstanding figure; what this view adds over the invoice
 * one is conversion. Convert turns the quote into an invoice through the same save pipeline a hand-keyed
 * invoice uses (a real number, a ledger charge, stock issued, a snapshot) and marks the quote converted —
 * once. A converted quote shows a link to the invoice it became instead of the convert button.
 */

import { useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useParams, useRouter } from "next/navigation";
import Link from "next/link";
import { ArrowLeft, ArrowRight, Download, Mail, Pencil, Printer, Trash2 } from "lucide-react";
import { ApiError } from "@/lib/api";
import { convertQuotation, deleteQuotation, emailQuotation, getQuotation, quotationRecipients } from "@/lib/quotations";
import { me } from "@/lib/auth";
import { today } from "@/lib/period";
import { PageHeader } from "@/components/shell/app-shell";
import { DataTable, downloadExcel, type ColumnDef } from "@/components/data-table";
import { formatMoney, formatReportDate } from "@/components/reports";
import { Badge, Button, Card, Dialog, ErrorBanner, FadeIn, Input, Select, Skeleton, toast } from "@/components/ui";
import { History } from "@/components/history/history";
import { PrintPreview } from "@/components/print-preview";
import { EmailDocumentDialog } from "@/components/email-document-dialog";
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
  const error = quotation.error as ApiError | null;
  const data = quotation.data;
  const converted = data?.convertedInvoiceId != null;
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
          {canModify && !converted && (
            <Button variant="secondary" onClick={() => router.push(`/quotations/${quotationId}/edit`)}>
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
          {/* Both new and legacy quotes convert — a legacy one is built from its stored lines. */}
          {data && !converted && (
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
              <Button variant="secondary" onClick={() => router.push(`/invoices/${data.convertedInvoiceId}`)}>
                View invoice {data.convertedInvoiceNumber}
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

          <ConvertDialog
            open={converting}
            onOpenChange={setConverting}
            quotationNumber={data.number}
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

          <VoidDialog
            open={voiding}
            onOpenChange={setVoiding}
            quotationNumber={data.number}
            onVoided={() => {
              void queryClient.invalidateQueries({ queryKey: ["quotations"] });
              toast.success(`Quotation ${data.number} voided.`);
              router.push("/quotations");
            }}
            voidQuotation={(reason) => deleteQuotation(quotationId, data.rowVersion, reason)}
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

function VoidDialog({ open, onOpenChange, quotationNumber, onVoided, voidQuotation }: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  quotationNumber: string;
  onVoided: () => void;
  voidQuotation: (reason: string) => Promise<unknown>;
}) {
  const [reason, setReason] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  async function submit() {
    setSubmitting(true);
    setError(null);
    try {
      await voidQuotation(reason);
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
      title={`Void quotation ${quotationNumber}`}
      description="The quotation is soft-deleted — recoverable and audited. A legacy quotation is adopted into the new system first."
      footer={
        <>
          <Button variant="secondary" onClick={() => onOpenChange(false)} disabled={submitting}>
            Cancel
          </Button>
          <Button onClick={submit} pending={submitting} disabled={reason.trim().length < 10}>
            Void quotation
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
          placeholder="Why is this quotation being voided?"
        />
      </div>
    </Dialog>
  );
}

/**
 * The terms that turn a quotation into an invoice — plus, for a SERVICE quotation, its cost.
 *
 * An item quotation already knows its cost basis: every line references an item, and the item master
 * carries that item's cost. A service quotation has no such source, so the figure is asked for here, at the
 * point the work is actually committed to and the cost is real. The server enforces the same rule and
 * refuses the conversion without it (400) — this field is the convenience, not the guarantee.
 */
function ConvertDialog({ open, onOpenChange, quotationNumber, isService, storedCost, defaultContact, onConverted, convert }: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  quotationNumber: string;
  /** Item vs service, decided by the server on whether any line resolves to an item — not by a stored flag. */
  isService: boolean;
  /** Any cost already recorded on the quote, used to seed the field so nothing captured earlier is retyped. */
  storedCost: number;
  defaultContact: string | null | undefined;
  onConverted: (invoiceId: number) => void;
  convert: (request: { type: string; date: string; purchaseOrderNo: string | null; contactPerson: string | null; documentCost: number | null }) => Promise<{ id: number; number: string; total: number }>;
}) {
  const [type, setType] = useState("Credit");
  const [date, setDate] = useState(today);
  const [po, setPo] = useState("");
  const [cost, setCost] = useState(storedCost > 0 ? String(storedCost) : "");
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  // Zero is a valid answer; blank is not. A service that genuinely cost nothing is a claim someone can make,
  // but it has to be made — defaulting a blank box to zero is exactly how an invoice comes to report 100%
  // margin with nobody having said so.
  const costEntered = cost.trim() !== "" && Number.isFinite(Number(cost)) && Number(cost) >= 0;
  const canConvert = !isService || costEntered;

  async function submit() {
    setSubmitting(true);
    setError(null);
    try {
      const invoice = await convert({
        type,
        date,
        purchaseOrderNo: po || null,
        contactPerson: defaultContact || null,
        // Only a service quotation sends one; the server derives an item quotation's from its lines and
        // ignores anything sent here.
        documentCost: isService ? Number(cost) : null,
      });
      toast.success(`Invoice ${invoice.number} raised from ${quotationNumber}.`);
      onOpenChange(false);
      onConverted(invoice.id);
    } catch (e) {
      // The server refuses a second conversion, and enforces the credit limit — both arrive as a 409.
      setError(e as ApiError);
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <Dialog
      open={open}
      onOpenChange={onOpenChange}
      title={`Convert ${quotationNumber} to an invoice`}
      description="The invoice is taxed at its own date and gets a real number, a ledger charge and — for item lines — a stock issue. This cannot be undone."
      footer={
        <>
          <Button variant="secondary" onClick={() => onOpenChange(false)} disabled={submitting}>
            Cancel
          </Button>
          <Button onClick={submit} pending={submitting} disabled={!canConvert}>
            Convert
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        {error && <ErrorBanner message={error.message} correlationId={error.correlationId} />}

        <Select label="Type" value={type} onChange={(e) => setType(e.target.value)}>
          <option value="Credit">Credit</option>
          <option value="Cash">Cash</option>
        </Select>

        <Input label="Invoice date" type="date" value={date} onChange={(e) => setDate(e.target.value)} />
        <Input label="PO number" value={po} onChange={(e) => setPo(e.target.value)} />

        {isService && (
          <Input
            label="Cost"
            inputMode="decimal"
            value={cost}
            onChange={(e) => setCost(e.target.value)}
            placeholder="0"
            hint="What this work costs you. Required — it is the basis for the invoice's profit, and an invoice with no cost reports as pure profit."
          />
        )}
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
