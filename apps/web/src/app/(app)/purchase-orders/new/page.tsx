"use client";

/**
 * Raise a purchase order — the create screen, Phase 6 slice 1.
 *
 * The same shared line-item editor the invoice and quotation screens use, addressed to a supplier. A PO
 * charges nothing and issues nothing — it is an order, not a sale or a receipt — so there is no cash/credit
 * type, no credit limit and no stock movement; item lines carry their item so the future goods receipt can
 * receive against them. The draft is held in the browser and posted whole, once (D4); one company VAT rate,
 * resolved by the server at the PO's date.
 *
 * What is typed here is also autosaved to the server as a *draft* (`useDraftAutosave`) — a scratchpad row
 * that takes no PO number and orders nothing, so a closed tab does not cost somebody the order. Raising it
 * still goes through the one create call; the draft is deleted once it has.
 */

import { useMemo, useState } from "react";
import { useRouter } from "next/navigation";
import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { ArrowLeft } from "lucide-react";
import { ApiError } from "@/lib/api";
import { createPurchaseOrder, getPurchaseOrderTaxRate } from "@/lib/purchase-orders";
import { listCompanies } from "@/lib/customers";
import { listSuppliers } from "@/lib/suppliers";
import { listItems } from "@/lib/items";
import { today } from "@/lib/period";
import { formatAmount, MINOR_UNITS_PER_MAJOR, QUANTITY_SCALE } from "@/lib/money";
import { cn } from "@/lib/cn";
import { DRAFT_PURCHASE_ORDER } from "@/lib/drafts";
import { PageHeader } from "@/components/shell/app-shell";
import { formatMoney, formatReportDate } from "@/components/reports";
import { Button, Card, ErrorBanner, FadeIn, Input, Select } from "@/components/ui";
import { toast } from "@/components/ui";
import { DraftNotices, DraftStatus } from "@/components/documents/draft-status";
import { useDraftAutosave, useDraftResume } from "@/components/documents/use-draft-autosave";
import {
  LineDraftEditor,
  linesArePostable,
  useDraftTotals,
  type DocumentKind,
  type DraftLine,
} from "@/components/documents/line-draft";
import { SupplierCombobox } from "@/components/documents/supplier-combobox";

/** The saved shape. Bump it when the state below changes meaning — see `readPayload`. */
const DRAFT_VERSION = 1;

interface PurchaseOrderDraftState {
  kind: DocumentKind;
  companyId: string;
  supplierId: string;
  date: string;
  documentDiscount: string;
  lines: DraftLine[];
}

export default function NewPurchaseOrderPage() {
  const router = useRouter();
  const companies = useQuery({ queryKey: ["companies"], queryFn: listCompanies });
  const suppliers = useQuery({ queryKey: ["suppliers"], queryFn: listSuppliers });
  const items = useQuery({ queryKey: ["items"], queryFn: listItems });

  const [kind, setKind] = useState<DocumentKind>("service");
  const [companyId, setCompanyId] = useState("");
  const [supplierId, setSupplierId] = useState("");
  const [date, setDate] = useState(today);
  const [documentDiscount, setDocumentDiscount] = useState("");
  const [lines, setLines] = useState<DraftLine[]>([]);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  const taxRate = useQuery({
    queryKey: ["purchase-order-tax-rate", companyId, date],
    queryFn: () => getPurchaseOrderTaxRate(Number(companyId), date),
    enabled: companyId !== "",
  });
  const rateError = taxRate.error as ApiError | null;
  const ratePercent = taxRate.data?.percentage ?? 0;

  const docPercent = useMemo(() => {
    const value = Number(documentDiscount);
    return Number.isFinite(value) ? Math.min(100, Math.max(0, value)) : 0;
  }, [documentDiscount]);

  const totals = useDraftTotals(lines, ratePercent, docPercent);
  const canSubmit = companyId !== "" && supplierId !== "" && linesArePostable(lines);

  // --- The draft ---------------------------------------------------------------------------------

  const selectedSupplier = suppliers.data?.find((s) => String(s.id) === supplierId) ?? null;

  const resume = useDraftResume<PurchaseOrderDraftState>(DRAFT_VERSION, (state) => {
    setKind(state.kind);
    setCompanyId(state.companyId);
    setSupplierId(state.supplierId);
    setDate(state.date);
    setDocumentDiscount(state.documentDiscount);
    setLines(state.lines);
  });

  const draft = useDraftAutosave<PurchaseOrderDraftState>({
    docType: DRAFT_PURCHASE_ORDER,
    version: DRAFT_VERSION,
    state: { kind, companyId, supplierId, date, documentDiscount, lines },
    // A company on its own is not work — it is the first thing the screen asks for. A supplier or a line
    // means somebody started building an order.
    worthKeeping: supplierId !== "" || lines.length > 0,
    summary: {
      partyName: selectedSupplier?.name ?? null,
      // Major units: the totals are held in fils, and the draft's total is stored the way a document's is.
      total: lines.length === 0 ? null : totals.total / MINOR_UNITS_PER_MAJOR,
      lineCount: lines.length,
    },
    resuming: resume.resuming,
    enabled: !resume.loading,
  });

  async function submit() {
    setSubmitting(true);
    setError(null);
    try {
      const created = await createPurchaseOrder({
        companyId: Number(companyId),
        supplierId: Number(supplierId),
        date,
        documentDiscountPercent: docPercent,
        lines: lines.map((l) => ({
          itemId: l.itemId,
          itemCode: l.itemCode,
          description: l.description,
          quantity: l.quantity / QUANTITY_SCALE,
          unitPrice: l.unitPrice / MINOR_UNITS_PER_MAJOR,
          discountPercent: l.discountPercent,
          cost: l.cost === null ? null : l.cost / MINOR_UNITS_PER_MAJOR,
        })),
      });
      // The order exists now, so the draft has nothing left to protect. Cleared before navigating so the
      // Drafts list is already right when the user goes back to it.
      draft.clear();
      toast.success(`Purchase order ${created.number} raised — ${formatMoney(created.total)}.`);
      router.push(`/purchase-orders/${created.id}`);
    } catch (e) {
      setError(e as ApiError);
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <FadeIn className="space-y-6">
      <Link
        href="/purchase-orders"
        className="inline-flex items-center gap-1.5 text-sm text-muted transition-colors hover:text-text"
      >
        <ArrowLeft className="size-4" aria-hidden />
        All purchase orders
      </Link>

      <PageHeader
        title={draft.draftId === null ? "New purchase order" : "Draft purchase order"}
        description="An order to a supplier — nothing is received into stock or owed until the goods receipt and the supplier invoice."
      />

      {error && <ErrorBanner message={error.message} correlationId={error.correlationId} />}

      <DraftNotices resume={resume} />
      <DraftStatus draft={draft} noun="purchase order" />

      <Card className="grid gap-4 p-5 sm:grid-cols-2 lg:grid-cols-3">
        <Select label="Company" value={companyId} onChange={(e) => setCompanyId(e.target.value)}>
          <option value="">Select…</option>
          {companies.data?.map((c) => (
            <option key={c.id} value={c.id}>{c.name}</option>
          ))}
        </Select>

        <SupplierCombobox
          suppliers={suppliers.data ?? []}
          value={supplierId}
          onChange={setSupplierId}
        />

        <Input label="Date" type="date" value={date} onChange={(e) => setDate(e.target.value)} />
      </Card>

      {rateError && <ErrorBanner message={rateError.message} correlationId={rateError.correlationId} />}

      <LineDraftEditor
        kind={kind}
        onKindChange={setKind}
        lines={lines}
        onLinesChange={setLines}
        items={items.data ?? []}
      />

      <div className="grid gap-4 sm:grid-cols-2">
        <div />
        <Card className="space-y-2 p-5">
          <Row label="Date" value={formatReportDate(date)} />
          <Row label="Subtotal" value={formatAmount(totals.subtotal)} />

          {totals.discount - totals.docDiscount > 0 && (
            <Row label="Line discounts" value={`− ${formatAmount(totals.discount - totals.docDiscount)}`} />
          )}

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

          {totals.docDiscount > 0 && (
            <Row label="Document discount" value={`− ${formatAmount(totals.docDiscount)}`} />
          )}

          <Row label="Net" value={formatAmount(totals.net)} />
          <Row
            label={companyId === "" ? "VAT" : taxRate.isPending ? "VAT (…)" : `VAT (${ratePercent}%)`}
            value={formatAmount(totals.vat)}
          />
          <div className="border-t border-subtle pt-2">
            <Row label="Total" value={formatAmount(totals.total)} strong />
          </div>

          <Button className="mt-2 w-full" onClick={submit} pending={submitting} disabled={!canSubmit}>
            Raise purchase order
          </Button>
        </Card>
      </div>
    </FadeIn>
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
