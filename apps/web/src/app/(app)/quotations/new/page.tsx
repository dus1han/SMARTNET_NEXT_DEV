"use client";

/**
 * Raise a quotation — the create screen, Phase 5 slice 3.
 *
 * The same shared line-item editor the invoice screen uses (`components/documents/line-draft`), given a
 * document that charges nothing and issues nothing. So there is no cash/credit type and no PO — a
 * quotation is a priced offer, not a sale — and it carries a validity (how long the price holds). The
 * draft is held in the browser and posted whole, once (D4); one company VAT rate, resolved by the server.
 *
 * What is typed here is also autosaved to the server as a *draft* (`useDraftAutosave`) — a scratchpad
 * row that takes no quotation number and raises nothing, so a closed tab or an expired session does not
 * cost somebody the whole quote. Raising it still goes through the one create call above; the draft is
 * deleted once it has.
 */

import { Suspense, useMemo, useState } from "react";
import { useRouter } from "next/navigation";
import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { ArrowLeft } from "lucide-react";
import { ApiError } from "@/lib/api";
import { createQuotation, getQuotationTaxRate } from "@/lib/quotations";
import { listCompanies, listCustomers } from "@/lib/customers";
import { listItems } from "@/lib/items";
import { today } from "@/lib/period";
import { formatAmount, MINOR_UNITS_PER_MAJOR, QUANTITY_SCALE } from "@/lib/money";
import { cn } from "@/lib/cn";
import { DRAFT_QUOTATION } from "@/lib/drafts";
import { PageHeader } from "@/components/shell/app-shell";
import { formatMoney, formatReportDate } from "@/components/reports";
import { Button, Card, ErrorBanner, FadeIn, Input, LoadingPanel, Select, toast } from "@/components/ui";
import { DraftNotices, DraftStatus } from "@/components/documents/draft-status";
import { useDraftAutosave, useDraftResume } from "@/components/documents/use-draft-autosave";
import {
  CustomerCombobox,
  customerContactNames,
  draftCostBasis,
  LineDraftEditor,
  linesArePostable,
  useDraftTotals,
  type DocumentKind,
  type DraftLine,
} from "@/components/documents/line-draft";

/** The saved shape. Bump it when the state below changes meaning — see `readPayload`. */
const DRAFT_VERSION = 1;

interface QuotationDraftState {
  kind: DocumentKind;
  companyId: string;
  customerId: string;
  date: string;
  validity: string;
  contact: string;
  documentDiscount: string;
  lines: DraftLine[];
}

/**
 * `useDraftResume` reads `?draft=` through `useSearchParams`, which forces the client tree up to the
 * nearest Suspense boundary to be client-rendered — so the boundary is here rather than swallowing the
 * route. Same reasoning as the sign-in screen.
 */
export default function NewQuotationPage() {
  return (
    <Suspense fallback={<LoadingPanel label="Opening…" />}>
      <NewQuotationForm />
    </Suspense>
  );
}

function NewQuotationForm() {
  const router = useRouter();
  const companies = useQuery({ queryKey: ["companies"], queryFn: listCompanies });
  const customers = useQuery({ queryKey: ["customers"], queryFn: listCustomers });
  const items = useQuery({ queryKey: ["items"], queryFn: listItems });

  const [kind, setKind] = useState<DocumentKind>("service");
  const [companyId, setCompanyId] = useState("");
  const [customerId, setCustomerId] = useState("");
  const [date, setDate] = useState(today);
  const [validity, setValidity] = useState("30 Days");
  const [contact, setContact] = useState("");
  const [documentDiscount, setDocumentDiscount] = useState("");
  const [lines, setLines] = useState<DraftLine[]>([]);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  const taxRate = useQuery({
    queryKey: ["quotation-tax-rate", companyId, date],
    queryFn: () => getQuotationTaxRate(Number(companyId), date),
    enabled: companyId !== "",
  });
  const rateError = taxRate.error as ApiError | null;
  const ratePercent = taxRate.data?.percentage ?? 0;

  const docPercent = useMemo(() => {
    const value = Number(documentDiscount);
    return Number.isFinite(value) ? Math.min(100, Math.max(0, value)) : 0;
  }, [documentDiscount]);

  const selectedCustomer = customers.data?.find((c) => String(c.id) === customerId) ?? null;
  const contactOptions = customerContactNames(selectedCustomer);

  const totals = useDraftTotals(lines, ratePercent, docPercent);

  // --- The draft ---------------------------------------------------------------------------------

  const resume = useDraftResume<QuotationDraftState>(DRAFT_VERSION, (state) => {
    setKind(state.kind);
    setCompanyId(state.companyId);
    setCustomerId(state.customerId);
    setDate(state.date);
    setValidity(state.validity);
    setContact(state.contact);
    setDocumentDiscount(state.documentDiscount);
    setLines(state.lines);
  });

  const draft = useDraftAutosave<QuotationDraftState>({
    docType: DRAFT_QUOTATION,
    version: DRAFT_VERSION,
    state: { kind, companyId, customerId, date, validity, contact, documentDiscount, lines },
    // A company on its own is not work — it is the first thing the screen asks for and is often the only
    // one. A customer or a line means somebody started building a quote.
    worthKeeping: customerId !== "" || lines.length > 0,
    summary: {
      partyName: selectedCustomer?.name ?? null,
      // Major units: the totals are held in fils, and the draft's total is stored the way a document's is.
      total: lines.length === 0 ? null : totals.total / MINOR_UNITS_PER_MAJOR,
      lineCount: lines.length,
    },
    resuming: resume.resuming,
    enabled: !resume.loading,
  });
  // Σ (unit cost × quantity), the same arithmetic the server will apply — so the read-only figure below is
  // the one that gets stored, not an approximation of it.
  const costBasis = draftCostBasis(lines);
  const canSubmit = companyId !== "" && customerId !== "" && linesArePostable(lines);

  async function submit() {
    setSubmitting(true);
    setError(null);
    try {
      const created = await createQuotation({
        companyId: Number(companyId),
        customerId: Number(customerId),
        date,
        contactPerson: contact || null,
        validity: validity || null,
        documentDiscountPercent: docPercent,
        // No documentCost from this screen, for either kind. An item quotation's cost is derived from its
        // line costs by the server; a SERVICE quotation's is not known yet — it is entered when the quote is
        // converted to an invoice, which is the point at which the work is committed to and the cost is real.
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
      // The quotation exists now, so the draft has nothing left to protect. Cleared before navigating so
      // the Drafts list is already right when the user goes back to it.
      draft.clear();
      toast.success(`Quotation ${created.number} raised — ${formatMoney(created.total)}.`);
      router.push(`/quotations/${created.id}`);
    } catch (e) {
      setError(e as ApiError);
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <FadeIn className="space-y-6">
      <Link
        href="/quotations"
        className="inline-flex items-center gap-1.5 text-sm text-muted transition-colors hover:text-text"
      >
        <ArrowLeft className="size-4" aria-hidden />
        All quotations
      </Link>

      <PageHeader
        title={draft.draftId === null ? "New quotation" : "Draft quotation"}
        description="A priced offer — nothing is charged or issued until it is converted to an invoice."
      />

      {error && <ErrorBanner message={error.message} correlationId={error.correlationId} />}

      <DraftNotices resume={resume} />
      <DraftStatus draft={draft} noun="quotation" />

      <Card className="grid gap-4 p-5 sm:grid-cols-2 lg:grid-cols-3">
        <Select label="Company" value={companyId} onChange={(e) => setCompanyId(e.target.value)}>
          <option value="">Select…</option>
          {companies.data?.map((c) => (
            <option key={c.id} value={c.id}>{c.name}</option>
          ))}
        </Select>

        <CustomerCombobox
          customers={customers.data ?? []}
          value={customerId}
          onChange={(id) => {
            setCustomerId(id);
            const picked = customers.data?.find((c) => String(c.id) === id);
            setContact(customerContactNames(picked)[0] ?? "");
          }}
        />

        <Input label="Date" type="date" value={date} onChange={(e) => setDate(e.target.value)} />
        <Input
          label="Valid for"
          value={validity}
          onChange={(e) => setValidity(e.target.value)}
          hint="How long the price holds — e.g. 30 Days."
        />

        {contactOptions.length > 0 ? (
          <Select label="Contact person" value={contact} onChange={(e) => setContact(e.target.value)}>
            {!contactOptions.includes(contact) && <option value="">Select…</option>}
            {contactOptions.map((person) => (
              <option key={person} value={person}>{person}</option>
            ))}
          </Select>
        ) : (
          <Input
            label="Contact person"
            value={contact}
            onChange={(e) => setContact(e.target.value)}
            hint={customerId === "" ? "Select a customer to pick from its contacts." : undefined}
          />
        )}
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

          {/* An ITEM quotation's cost is derived, not entered: every line references an item, and the item
              master carries its cost. Shown read-only so the margin is visible while quoting, but not
              typeable — the master is the authority, and a figure typed over it would be a second, private
              cost basis that nothing else in the system knows about.

              A SERVICE quotation shows nothing here. Its cost cannot be derived, is not known while quoting,
              and is asked for at conversion instead. */}
          {kind === "item" && (
            <div className="flex items-center justify-between gap-3 border-t border-subtle pt-2">
              <span className="text-muted">Cost</span>
              <span className="tabular text-text">{formatAmount(costBasis)}</span>
            </div>
          )}

          <Button className="mt-2 w-full" onClick={submit} pending={submitting} disabled={!canSubmit}>
            Raise quotation
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
