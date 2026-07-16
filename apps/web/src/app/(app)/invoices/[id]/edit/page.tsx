"use client";

/**
 * Edit an issued invoice — Phase 5 slice 5.
 *
 * The same shared line-item editor the create screen uses, loaded with the invoice's lines. Each existing
 * line carries its server id (encoded in the draft key, `srv-{id}`), so the save reconciles in place —
 * updated / added / removed — rather than deleting and re-inserting. The rate is the invoice's snapshot,
 * so an edit corrects figures without re-rating. A reason is mandatory. Company, customer, type and date
 * are the invoice's identity and are not editable here.
 */

import { useMemo, useState } from "react";
import { useParams, useRouter } from "next/navigation";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { ArrowLeft } from "lucide-react";
import { ApiError } from "@/lib/api";
import { editInvoice, getInvoice, type InvoiceDetail } from "@/lib/invoices";
import { listItems } from "@/lib/items";
import { formatAmount, MINOR_UNITS_PER_MAJOR, QUANTITY_SCALE } from "@/lib/money";
import { cn } from "@/lib/cn";
import { PageHeader } from "@/components/shell/app-shell";
import { formatReportDate } from "@/components/reports";
import { Button, Card, ErrorBanner, FadeIn, Input, Skeleton, toast } from "@/components/ui";
import {
  LineDraftEditor,
  linesArePostable,
  toMinor,
  useDraftTotals,
  type DocumentKind,
  type DraftLine,
} from "@/components/documents/line-draft";

/** Existing lines round-trip their server id through the draft key; a new line has none. */
const idFromKey = (key: string): number | null => (key.startsWith("srv-") ? Number(key.slice(4)) : null);

const seedLines = (invoice: InvoiceDetail): DraftLine[] =>
  invoice.lines.map((l, i): DraftLine => ({
    key: l.id != null ? `srv-${l.id}` : `line-${i}`,
    itemId: l.itemId ?? null,
    itemCode: l.itemCode ?? null,
    description: l.description ?? "",
    quantity: Math.round(l.quantity * QUANTITY_SCALE),
    unitPrice: toMinor(l.unitPrice),
    discountPercent: l.discountPercent,
    cost: l.cost == null ? null : toMinor(l.cost),
  }));

export default function EditInvoicePage() {
  const { id } = useParams<{ id: string }>();
  const invoiceId = Number(id);

  const invoice = useQuery({
    queryKey: ["invoice", invoiceId],
    queryFn: () => getInvoice(invoiceId),
    enabled: Number.isFinite(invoiceId),
  });
  const data = invoice.data;
  const error = invoice.error as ApiError | null;

  return (
    <FadeIn className="space-y-6">
      <Link
        href={`/invoices/${invoiceId}`}
        className="inline-flex items-center gap-1.5 text-sm text-muted transition-colors hover:text-text"
      >
        <ArrowLeft className="size-4" aria-hidden />
        Back to invoice
      </Link>

      <PageHeader
        title={data ? `Edit invoice ${data.number}` : "Edit invoice"}
        description="Correct the lines, PO, contact or discount. The change is versioned and needs a reason; a concurrent edit is rejected."
      />

      {invoice.isPending && <Skeleton className="h-40" />}
      {error && <ErrorBanner message={error.message} correlationId={error.correlationId} />}

      {data && data.origin === "legacy" && (
        <p className="rounded-lg border border-subtle bg-surface-sunken p-3 text-sm text-muted">
          This is a legacy invoice. Saving adopts it into the new system — its figures are recomputed and it
          gains a change history from this point.
        </p>
      )}

      {/* Mount the form only once the invoice is loaded, so its state seeds from real data exactly once. */}
      {data && <EditForm invoice={data} invoiceId={invoiceId} />}
    </FadeIn>
  );
}

function EditForm({ invoice, invoiceId }: { invoice: InvoiceDetail; invoiceId: number }) {
  const router = useRouter();
  const queryClient = useQueryClient();
  const items = useQuery({ queryKey: ["items"], queryFn: listItems });

  const [kind, setKind] = useState<DocumentKind>(invoice.lines.some((l) => l.itemId != null) ? "item" : "service");
  const [po, setPo] = useState(invoice.purchaseOrderNo ?? "");
  const [contact, setContact] = useState(invoice.contactPerson ?? "");
  const [documentDiscount, setDocumentDiscount] = useState(
    invoice.documentDiscountPercent > 0 ? String(invoice.documentDiscountPercent) : "",
  );
  const [reason, setReason] = useState("");
  const [lines, setLines] = useState<DraftLine[]>(() => seedLines(invoice));
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  const ratePercent = invoice.taxRatePercentage;
  const docPercent = useMemo(() => {
    const value = Number(documentDiscount);
    return Number.isFinite(value) ? Math.min(100, Math.max(0, value)) : 0;
  }, [documentDiscount]);

  const totals = useDraftTotals(lines, ratePercent, docPercent);
  const canSubmit = linesArePostable(lines) && reason.trim().length >= 10;

  async function submit() {
    setSubmitting(true);
    setError(null);
    try {
      const edited = await editInvoice(
        invoiceId,
        {
          expectedRowVersion: invoice.rowVersion,
          purchaseOrderNo: po || null,
          contactPerson: contact || null,
          documentDiscountPercent: docPercent,
          lines: lines.map((l) => ({
            id: idFromKey(l.key),
            itemId: l.itemId,
            itemCode: l.itemCode,
            description: l.description,
            quantity: l.quantity / QUANTITY_SCALE,
            unitPrice: l.unitPrice / MINOR_UNITS_PER_MAJOR,
            discountPercent: l.discountPercent,
            cost: l.cost === null ? null : l.cost / MINOR_UNITS_PER_MAJOR,
          })),
        },
        reason,
      );
      toast.success(`Invoice ${edited.number} updated — version ${edited.versionNo}.`);
      void queryClient.invalidateQueries({ queryKey: ["invoice", invoiceId] });
      router.push(`/invoices/${invoiceId}`);
    } catch (e) {
      // A stale row_version and a payment against the invoice both arrive as a 409.
      setError(e as ApiError);
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <>
      {error && <ErrorBanner message={error.message} correlationId={error.correlationId} />}

      <Card className="grid gap-4 p-5 sm:grid-cols-2">
        <Input label="PO number" value={po} onChange={(e) => setPo(e.target.value)} />
        <Input label="Contact person" value={contact} onChange={(e) => setContact(e.target.value)} />
      </Card>

      <LineDraftEditor kind={kind} onKindChange={setKind} lines={lines} onLinesChange={setLines} items={items.data ?? []} />

      <div className="grid gap-4 sm:grid-cols-2">
        <Card className="space-y-3 p-5">
          <Input
            label="Reason for the change"
            value={reason}
            onChange={(e) => setReason(e.target.value)}
            hint="At least 10 characters — recorded on the audit trail and the new version."
            placeholder="Why is this invoice being changed?"
          />
        </Card>

        <Card className="space-y-2 p-5">
          <Row label="Date" value={formatReportDate(invoice.date)} />
          <Row label="Subtotal" value={formatAmount(totals.subtotal)} />

          <div className="flex items-center justify-between gap-3 py-0.5">
            <label htmlFor="doc-discount" className="text-muted">Document discount %</label>
            <input
              id="doc-discount"
              inputMode="decimal"
              value={documentDiscount}
              onChange={(e) => setDocumentDiscount(e.target.value)}
              placeholder="0"
              className={cn(
                "w-20 rounded border border-subtle bg-surface px-2 py-1 text-right tabular text-text",
                "focus:border-primary focus:outline-none focus:ring-2 focus:ring-ring/25",
              )}
            />
          </div>

          <Row label="Net" value={formatAmount(totals.net)} />
          <Row label={`VAT (${ratePercent}%)`} value={formatAmount(totals.vat)} />
          <div className="border-t border-subtle pt-2">
            <Row label="Total" value={formatAmount(totals.total)} strong />
          </div>

          <Button className="mt-2 w-full" onClick={submit} pending={submitting} disabled={!canSubmit}>
            Save changes
          </Button>
        </Card>
      </div>
    </>
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
