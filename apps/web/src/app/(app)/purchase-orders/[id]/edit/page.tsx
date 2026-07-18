"use client";

/**
 * Edit a purchase order.
 *
 * The same shared line-item editor the create screen uses, loaded with the order's lines. Each existing
 * line carries its server id (encoded in the draft key, `srv-{id}`), so the save reconciles in place —
 * updated / added / removed — rather than deleting and re-inserting. A reason is mandatory.
 *
 * Company and supplier are the order's identity and are not editable here; the date is, and moving it
 * re-rates the order at the VAT rate in force then. An order posts no ledger entry and no stock movement,
 * so an edit re-values the document and nothing else.
 */

import { useMemo, useState } from "react";
import { useParams, useRouter } from "next/navigation";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { ArrowLeft } from "lucide-react";
import { ApiError } from "@/lib/api";
import { editPurchaseOrder, getPurchaseOrder, type PurchaseOrderDetail } from "@/lib/purchase-orders";
import { listItems } from "@/lib/items";
import { formatAmount, MINOR_UNITS_PER_MAJOR, QUANTITY_SCALE } from "@/lib/money";
import { PageHeader } from "@/components/shell/app-shell";
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

const seedLines = (order: PurchaseOrderDetail): DraftLine[] =>
  order.lines.map((l, i): DraftLine => ({
    key: l.id != null ? `srv-${l.id}` : `line-${i}`,
    itemId: l.itemId ?? null,
    itemCode: l.itemCode ?? null,
    description: l.description ?? "",
    quantity: Math.round(l.quantity * QUANTITY_SCALE),
    unitPrice: toMinor(l.unitPrice),
    discountPercent: l.discountPercent,
    cost: l.cost == null ? null : toMinor(l.cost),
  }));

export default function EditPurchaseOrderPage() {
  const { id } = useParams<{ id: string }>();
  const orderId = Number(id);

  const order = useQuery({
    queryKey: ["purchase-order", orderId],
    queryFn: () => getPurchaseOrder(orderId),
    enabled: Number.isFinite(orderId),
  });

  const data = order.data;
  const error = order.error as ApiError | null;

  return (
    <FadeIn className="space-y-6">
      <Link
        href={`/purchase-orders/${orderId}`}
        className="inline-flex items-center gap-1.5 text-sm text-muted transition-colors hover:text-text"
      >
        <ArrowLeft className="size-4" aria-hidden />
        Back to purchase order
      </Link>

      <PageHeader
        title={data ? `Edit purchase order ${data.number}` : "Edit purchase order"}
        description="Correct the lines, discount or date. Versioned and reason-gated; a concurrent edit is rejected."
      />

      {order.isPending && <Skeleton className="h-40" />}
      {error && <ErrorBanner message={error.message} correlationId={error.correlationId} />}

      {/* Mount the form only once loaded, so its state seeds from real data exactly once. */}
      {data && <EditForm order={data} orderId={orderId} />}
    </FadeIn>
  );
}

function EditForm({ order, orderId }: { order: PurchaseOrderDetail; orderId: number }) {
  const router = useRouter();
  const queryClient = useQueryClient();
  const items = useQuery({ queryKey: ["items"], queryFn: listItems });

  const [kind, setKind] = useState<DocumentKind>(order.lines.some((l) => l.itemId != null) ? "item" : "service");
  const [date, setDate] = useState(order.date);
  const [documentDiscount, setDocumentDiscount] = useState(
    order.documentDiscountPercent > 0 ? String(order.documentDiscountPercent) : "",
  );
  const [reason, setReason] = useState("");
  const [lines, setLines] = useState<DraftLine[]>(() => seedLines(order));
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  const ratePercent = order.taxRatePercentage;
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
      const edited = await editPurchaseOrder(
        orderId,
        {
          expectedRowVersion: order.rowVersion,
          // Only sent when actually moved; a changed date re-rates the order server-side.
          date: date !== order.date ? date : null,
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
      toast.success(`Purchase order ${edited.number} updated — version ${edited.versionNo}.`);
      void queryClient.invalidateQueries({ queryKey: ["purchase-order", orderId] });
      void queryClient.invalidateQueries({ queryKey: ["purchase-orders"] });
      router.push(`/purchase-orders/${orderId}`);
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
            date !== order.date
              ? "Moving the date re-rates this order at the VAT rate in force then."
              : undefined
          }
        />
        <Input
          label="Document discount %"
          inputMode="decimal"
          value={documentDiscount}
          onChange={(e) => setDocumentDiscount(e.target.value)}
          placeholder="0"
        />
      </Card>

      <LineDraftEditor kind={kind} onKindChange={setKind} lines={lines} onLinesChange={setLines} items={items.data ?? []} />

      <div className="grid gap-4 sm:grid-cols-2">
        <Card className="space-y-3 p-5">
          <Input
            label="Reason for the change"
            value={reason}
            onChange={(e) => setReason(e.target.value)}
            hint="At least 10 characters — recorded on the audit trail and the version."
            placeholder="Why is this order being changed?"
          />
        </Card>

        <Card className="space-y-2 p-5">
          <Row label="Subtotal" value={formatAmount(totals.subtotal)} />
          {totals.discount > 0 && <Row label="Discount" value={`− ${formatAmount(totals.discount)}`} />}
          <Row label="Net" value={formatAmount(totals.net)} />
          <Row label={`VAT (${ratePercent}%)`} value={formatAmount(totals.vat)} />
          <div className="border-t border-subtle pt-2">
            <Row label="Total" value={formatAmount(totals.total)} strong />
          </div>
        </Card>
      </div>

      <div className="flex justify-end gap-2">
        <Button variant="secondary" onClick={() => router.push(`/purchase-orders/${orderId}`)} disabled={submitting}>
          Cancel
        </Button>
        <Button onClick={submit} pending={submitting} disabled={!canSubmit}>
          Save changes
        </Button>
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
