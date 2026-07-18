"use client";

/**
 * Edit a quotation — Phase 5 slice 5 (legacy parity).
 *
 * The same shared line-item editor as invoices, adapted for a quotation: no cash/credit type and no PO, but
 * a validity. Each existing line round-trips its server id through the draft key (`srv-{id}`), so the save
 * reconciles in place. A legacy quotation is adopted on save; a converted one cannot be edited (the view
 * hides the button, and the server refuses it). A reason is mandatory.
 */

import { useMemo, useState } from "react";
import { useParams, useRouter } from "next/navigation";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { ArrowLeft } from "lucide-react";
import { ApiError } from "@/lib/api";
import { editQuotation, getQuotation, type QuotationDetail } from "@/lib/quotations";
import { listItems } from "@/lib/items";
import { formatAmount, MINOR_UNITS_PER_MAJOR, QUANTITY_SCALE } from "@/lib/money";
import { cn } from "@/lib/cn";
import { PageHeader } from "@/components/shell/app-shell";
import { formatReportDate } from "@/components/reports";
import { Button, Card, ErrorBanner, FadeIn, Input, Select, Skeleton, toast } from "@/components/ui";
import {
  customerContactNames,
  LineDraftEditor,
  linesArePostable,
  toMinor,
  useDraftTotals,
  type DocumentKind,
  type DraftLine,
} from "@/components/documents/line-draft";
import { listCustomers } from "@/lib/customers";

const idFromKey = (key: string): number | null => (key.startsWith("srv-") ? Number(key.slice(4)) : null);

const seedLines = (q: QuotationDetail): DraftLine[] =>
  q.lines.map((l, i): DraftLine => ({
    key: l.id != null ? `srv-${l.id}` : `line-${i}`,
    itemId: l.itemId ?? null,
    itemCode: l.itemCode ?? null,
    description: l.description ?? "",
    quantity: Math.round(l.quantity * QUANTITY_SCALE),
    unitPrice: toMinor(l.unitPrice),
    discountPercent: l.discountPercent,
    cost: l.cost == null ? null : toMinor(l.cost),
  }));

export default function EditQuotationPage() {
  const { id } = useParams<{ id: string }>();
  const quotationId = Number(id);

  const quotation = useQuery({
    queryKey: ["quotation", quotationId],
    queryFn: () => getQuotation(quotationId),
    enabled: Number.isFinite(quotationId),
  });
  const data = quotation.data;
  const error = quotation.error as ApiError | null;

  return (
    <FadeIn className="space-y-6">
      <Link
        href={`/quotations/${quotationId}`}
        className="inline-flex items-center gap-1.5 text-sm text-muted transition-colors hover:text-text"
      >
        <ArrowLeft className="size-4" aria-hidden />
        Back to quotation
      </Link>

      <PageHeader
        title={data ? `Edit quotation ${data.number}` : "Edit quotation"}
        description="Correct the lines, discount, contact or validity. Versioned and reason-gated; a concurrent edit is rejected."
      />

      {quotation.isPending && <Skeleton className="h-40" />}
      {error && <ErrorBanner message={error.message} correlationId={error.correlationId} />}

      {data && data.origin === "legacy" && (
        <p className="rounded-lg border border-subtle bg-surface-sunken p-3 text-sm text-muted">
          This is a legacy quotation. Saving adopts it into the new system — its figures are recomputed and
          it gains a change history from this point.
        </p>
      )}

      {data && <EditForm quotation={data} quotationId={quotationId} />}
    </FadeIn>
  );
}

function EditForm({ quotation, quotationId }: { quotation: QuotationDetail; quotationId: number }) {
  const router = useRouter();
  const queryClient = useQueryClient();
  const items = useQuery({ queryKey: ["items"], queryFn: listItems });
  const customers = useQuery({ queryKey: ["customers"], queryFn: listCustomers });

  // The same pick-list the New Quotation screen offers, so an edit is not the one screen where the
  // contact has to be retyped from memory. customerContactNames returns the customer's *document*
  // contacts only — a notifications-only contact receives mail but is never printed on a document.
  const contactOptions = customerContactNames(
    customers.data?.find((c) => c.code === quotation.customerCode),
  );

  const [kind, setKind] = useState<DocumentKind>(quotation.lines.some((l) => l.itemId != null) ? "item" : "service");
  const [contact, setContact] = useState(quotation.contactPerson ?? "");
  const [date, setDate] = useState(quotation.date);
  const [validity, setValidity] = useState(quotation.validity ?? "30 Days");
  const [documentDiscount, setDocumentDiscount] = useState(
    quotation.documentDiscountPercent > 0 ? String(quotation.documentDiscountPercent) : "",
  );
  // A service quotation's document-level cost, seeded from the loaded quote so an edit does not wipe it.
  const [serviceCost, setServiceCost] = useState(quotation.cost > 0 ? String(quotation.cost) : "");
  const [reason, setReason] = useState("");
  const [lines, setLines] = useState<DraftLine[]>(() => seedLines(quotation));
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  const ratePercent = quotation.taxRatePercentage;
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
      const edited = await editQuotation(
        quotationId,
        {
          expectedRowVersion: quotation.rowVersion,
          // Only sent when actually moved; a changed date re-rates the quote server-side.
          date: date !== quotation.date ? date : null,
          contactPerson: contact || null,
          validity: validity || null,
          documentDiscountPercent: docPercent,
          // Service quotations carry a document-level cost; item quotations derive it from the line item costs.
          documentCost: kind === "service" && serviceCost !== "" ? Number(serviceCost) : null,
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
      toast.success(`Quotation ${edited.number} updated — version ${edited.versionNo}.`);
      void queryClient.invalidateQueries({ queryKey: ["quotation", quotationId] });
      router.push(`/quotations/${quotationId}`);
    } catch (e) {
      setError(e as ApiError);
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <>
      {error && <ErrorBanner message={error.message} correlationId={error.correlationId} />}

      <Card className="grid gap-4 p-5 sm:grid-cols-2">
        <Input
          label="Date"
          type="date"
          value={date}
          onChange={(e) => setDate(e.target.value)}
          hint={
            date !== quotation.date
              ? "Moving the date re-quotes this at the VAT rate in force then."
              : undefined
          }
        />
        {contactOptions.length > 0 ? (
          <Select label="Contact person" value={contact} onChange={(e) => setContact(e.target.value)}>
            {/* The stored contact may no longer be one of the customer's — keep it selectable rather
                than silently swapping it for another name. */}
            {!contactOptions.includes(contact) && <option value={contact}>{contact || "Select…"}</option>}
            {contactOptions.map((person) => (
              <option key={person} value={person}>{person}</option>
            ))}
          </Select>
        ) : (
          <Input
            label="Contact person"
            value={contact}
            onChange={(e) => setContact(e.target.value)}
            hint={customers.isPending ? "Loading contacts…" : "This customer has no document contacts on file."}
          />
        )}
        <Input label="Valid for" value={validity} onChange={(e) => setValidity(e.target.value)} hint="e.g. 30 Days." />
      </Card>

      <LineDraftEditor kind={kind} onKindChange={setKind} lines={lines} onLinesChange={setLines} items={items.data ?? []} />

      <div className="grid gap-4 sm:grid-cols-2">
        <Card className="space-y-3 p-5">
          <Input
            label="Reason for the change"
            value={reason}
            onChange={(e) => setReason(e.target.value)}
            hint="At least 10 characters — recorded on the audit trail and the new version."
            placeholder="Why is this quotation being changed?"
          />
        </Card>

        <Card className="space-y-2 p-5">
          <Row label="Date" value={formatReportDate(quotation.date)} />
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

          {/* A service quotation's cost (item quotations derive it from the item master) — the margin basis. */}
          {kind === "service" && (
            <div className="flex items-center justify-between gap-3 border-t border-subtle pt-2">
              <label htmlFor="service-cost" className="text-muted">Cost</label>
              <input
                id="service-cost"
                inputMode="decimal"
                value={serviceCost}
                onChange={(e) => setServiceCost(e.target.value)}
                placeholder="0"
                className={cn(
                  "w-28 rounded border border-subtle bg-surface px-2 py-1 text-right tabular text-text",
                  "focus:border-primary focus:outline-none focus:ring-2 focus:ring-ring/25",
                )}
              />
            </div>
          )}

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
