"use client";

/**
 * Raise a credit note — the create screen, Phase 5 slice 4.
 *
 * A credit note is raised against a parent invoice: pick the invoice, and its lines seed the same shared
 * line-item editor invoices and quotations use. Trim the lines or adjust the quantities to credit part of
 * it, choose whether the goods come back into stock, and post. The customer, company and VAT rate are
 * inherited from the invoice — a full credit nets exactly against it. The draft lives in the browser and is
 * posted whole, once (D4).
 */

import { useEffect, useId, useMemo, useRef, useState, type KeyboardEvent } from "react";
import { useRouter } from "next/navigation";
import { keepPreviousData, useQuery, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { ArrowLeft, Search } from "lucide-react";
import { ApiError } from "@/lib/api";
import { createCreditNote } from "@/lib/credit-notes";
import { getInvoice, getInvoices, type InvoiceSummary } from "@/lib/invoices";
import { today } from "@/lib/period";
import { formatAmount, MINOR_UNITS_PER_MAJOR, QUANTITY_SCALE } from "@/lib/money";
import { cn } from "@/lib/cn";
import { PageHeader } from "@/components/shell/app-shell";
import { formatMoney, formatReportDate } from "@/components/reports";
import { Button, Card, Checkbox, ErrorBanner, FadeIn, Input, Skeleton, toast } from "@/components/ui";
import {
  LineDraftEditor,
  linesArePostable,
  toMinor,
  useDraftTotals,
  type DocumentKind,
  type DraftLine,
} from "@/components/documents/line-draft";

export default function NewCreditNotePage() {
  const router = useRouter();
  const queryClient = useQueryClient();
  // The picker searches on the server now that the list is paged, so every invoice stays findable
  // rather than only the first page of them. Fifty matches is more than anyone scrolls.
  const [pickerSearch, setPickerSearch] = useState("");
  const invoices = useQuery({
    queryKey: ["invoices", "picker", pickerSearch],
    queryFn: () => getInvoices({ page: 1, pageSize: 50, search: pickerSearch }),
    placeholderData: keepPreviousData,
  });

  const [invoiceId, setInvoiceId] = useState<number | null>(null);
  const [date, setDate] = useState(today);
  const [returnsStock, setReturnsStock] = useState(false);
  const [kind, setKind] = useState<DocumentKind>("service");
  const [lines, setLines] = useState<DraftLine[]>([]);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  // The parent invoice, loaded in full so its lines seed the draft and its rate drives the preview.
  const invoice = useQuery({
    queryKey: ["invoice", invoiceId],
    queryFn: () => getInvoice(invoiceId as number),
    enabled: invoiceId != null,
  });
  const parent = invoice.data;
  const ratePercent = parent?.taxRatePercentage ?? 0;

  // Seed the draft when the invoice is picked — imperatively, not in an effect, so trimming lines
  // afterwards is never clobbered by a re-render. The item/service kind follows the invoice; items default
  // to returning stock, a pure service credit does not.
  async function pickInvoice(id: number) {
    setInvoiceId(id);
    const detail = await queryClient.fetchQuery({ queryKey: ["invoice", id], queryFn: () => getInvoice(id) });
    const hasItems = detail.lines.some((l) => l.itemId != null);
    setKind(hasItems ? "item" : "service");
    setReturnsStock(hasItems);
    setLines(detail.lines.map((l, i): DraftLine => ({
      key: `cn-${detail.id}-${i}`,
      itemId: l.itemId ?? null,
      itemCode: l.itemCode ?? null,
      description: l.description ?? "",
      quantity: Math.round(l.quantity * QUANTITY_SCALE),
      unitPrice: toMinor(l.unitPrice),
      discountPercent: l.discountPercent,
      cost: l.cost == null ? null : toMinor(l.cost),
    })));
  }

  const totals = useDraftTotals(lines, ratePercent, 0);
  const canSubmit = invoiceId != null && linesArePostable(lines);

  async function submit() {
    if (invoiceId == null) return;
    setSubmitting(true);
    setError(null);
    try {
      const created = await createCreditNote({
        invoiceId,
        date,
        returnsStock,
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
      toast.success(`Credit note ${created.number} raised — ${formatMoney(created.total)}.`);
      router.push(`/credit-notes/${created.id}`);
    } catch (e) {
      setError(e as ApiError);
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <FadeIn className="space-y-6">
      <Link
        href="/credit-notes"
        className="inline-flex items-center gap-1.5 text-sm text-muted transition-colors hover:text-text"
      >
        <ArrowLeft className="size-4" aria-hidden />
        All credit notes
      </Link>

      <PageHeader
        title="New credit note"
        description="Raised against an invoice — it reduces what the customer owes, and can return the goods to stock."
      />

      {error && <ErrorBanner message={error.message} correlationId={error.correlationId} />}

      <Card className="grid gap-4 p-5 sm:grid-cols-2 lg:grid-cols-3">
        <div className="lg:col-span-2">
          <InvoiceCombobox
            invoices={invoices.data?.rows ?? []}
            onQueryChange={setPickerSearch}
            value={invoiceId}
            onChange={pickInvoice}
          />
        </div>
        <Input label="Date" type="date" value={date} onChange={(e) => setDate(e.target.value)} />
      </Card>

      {invoiceId == null ? (
        <Card className="p-8 text-center text-muted">
          Pick the invoice this credit note is against — its lines will seed the note below.
        </Card>
      ) : invoice.isPending ? (
        <Skeleton className="h-40" />
      ) : (
        <>
          {parent && (
            <Card className="flex flex-wrap items-center justify-between gap-3 p-4">
              <div>
                <p className="font-medium text-text">
                  Crediting invoice {parent.number}
                  <span className="ml-2 text-sm text-muted">{formatReportDate(parent.date)}</span>
                </p>
                <p className="text-sm text-muted">
                  {parent.customerName ?? "—"} · {parent.companyName ?? "—"} · VAT {ratePercent}% (inherited)
                </p>
              </div>
              <span className="tabular text-sm text-muted">Invoice total {formatMoney(parent.total)}</span>
            </Card>
          )}

          <LineDraftEditor
            kind={kind}
            onKindChange={setKind}
            lines={lines}
            onLinesChange={setLines}
            items={[]}
          />

          <div className="grid gap-4 sm:grid-cols-2">
            <Card className="space-y-3 p-5">
              <p className="text-sm font-semibold text-text">Stock</p>
              <Checkbox
                label="Return the goods to stock"
                hint="Tick when the customer is sending the items back. A pure price adjustment leaves stock untouched."
                checked={returnsStock}
                onChange={(e) => setReturnsStock(e.target.checked)}
              />
            </Card>

            <Card className="space-y-2 p-5">
              <Row label="Date" value={formatReportDate(date)} />
              <Row label="Subtotal" value={formatAmount(totals.subtotal)} />
              {totals.discount > 0 && <Row label="Discounts" value={`− ${formatAmount(totals.discount)}`} />}
              <Row label="Net" value={formatAmount(totals.net)} />
              <Row label={`VAT (${ratePercent}%)`} value={formatAmount(totals.vat)} />
              <div className="border-t border-subtle pt-2">
                <Row label="Credit" value={formatAmount(totals.total)} strong />
              </div>

              <Button className="mt-2 w-full" onClick={submit} pending={submitting} disabled={!canSubmit}>
                Raise credit note
              </Button>
            </Card>
          </div>
        </>
      )}
    </FadeIn>
  );
}

/**
 * The invoice field, searchable — the list of raised invoices is long and a native dropdown of it is one
 * nobody can find their invoice in. Type a number or any word in the customer name; arrow and Enter, or
 * click. The same shape as the customer combobox in the shared editor.
 */
function InvoiceCombobox({ invoices, value, onChange, onQueryChange }: {
  invoices: readonly InvoiceSummary[];
  value: number | null;
  onChange: (id: number) => void;
  /** Reports what the user typed, so the server can search beyond the invoices already loaded. */
  onQueryChange: (query: string) => void;
}) {
  const listboxId = useId();
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState("");
  const [highlighted, setHighlighted] = useState(0);
  const blurTimer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);

  // Debounced, so typing an invoice number is one request rather than one per character.
  useEffect(() => {
    const timer = setTimeout(() => onQueryChange(query), 300);
    return () => clearTimeout(timer);
    // eslint-disable-next-line react-hooks/exhaustive-deps -- the callback is stable enough; the term is what matters
  }, [query]);

  const selected = invoices.find((i) => i.id === value) ?? null;
  const results = useMemo(() => searchInvoices(query, invoices), [query, invoices]);
  const active = results[Math.min(highlighted, results.length - 1)];

  const choose = (invoice: InvoiceSummary) => {
    onChange(invoice.id);
    setQuery("");
    setOpen(false);
    setHighlighted(0);
  };

  const onKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    switch (event.key) {
      case "ArrowDown":
        event.preventDefault();
        setOpen(true);
        setHighlighted((current) => Math.min(current + 1, results.length - 1));
        break;
      case "ArrowUp":
        event.preventDefault();
        setHighlighted((current) => Math.max(current - 1, 0));
        break;
      case "Enter":
        event.preventDefault();
        if (open && active) choose(active);
        break;
      case "Escape":
        setOpen(false);
        setQuery("");
        break;
    }
  };

  const shownValue = open ? query : selected ? `${selected.number} — ${selected.customerName ?? ""}` : "";

  return (
    <div className="space-y-1.5">
      <label className="block text-sm font-medium text-text">Invoice to credit</label>

      <div className="relative">
        <div className="flex items-center gap-2 rounded-md border border-subtle bg-surface px-3 focus-within:border-strong focus-within:ring-2 focus-within:ring-ring/25">
          <Search className="size-4 shrink-0 text-muted" aria-hidden />
          <input
            role="combobox"
            aria-expanded={open}
            aria-controls={listboxId}
            aria-autocomplete="list"
            value={shownValue}
            placeholder="Search invoices by number or customer…"
            onFocus={() => setOpen(true)}
            onChange={(event) => {
              setQuery(event.target.value);
              setOpen(true);
              setHighlighted(0);
            }}
            onKeyDown={onKeyDown}
            onBlur={() => { blurTimer.current = setTimeout(() => setOpen(false), 120); }}
            className="w-full bg-transparent py-2 text-sm text-text placeholder:text-muted focus:outline-none"
          />
        </div>

        {open && results.length > 0 && (
          <ul
            role="listbox"
            id={listboxId}
            className="absolute top-full z-10 mt-1 max-h-64 w-full overflow-auto rounded-lg border border-subtle bg-surface shadow-lg"
          >
            {results.map((invoice, index) => (
              <li key={invoice.id}>
                <button
                  type="button"
                  role="option"
                  aria-selected={invoice === active}
                  onMouseDown={(event) => {
                    event.preventDefault();
                    choose(invoice);
                  }}
                  onMouseEnter={() => setHighlighted(index)}
                  className={cn(
                    "flex w-full items-center gap-3 px-3 py-2 text-left text-sm",
                    invoice === active ? "bg-primary-ghost text-primary" : "text-text",
                  )}
                >
                  <span className="w-28 shrink-0 font-mono text-xs text-muted">{invoice.number}</span>
                  <span className="min-w-0 flex-1 truncate">{invoice.customerName ?? "—"}</span>
                  <span className="tabular text-muted">{formatMoney(invoice.total)}</span>
                </button>
              </li>
            ))}
          </ul>
        )}

        {open && query.trim() !== "" && results.length === 0 && (
          <div className="absolute top-full z-10 mt-1 w-full rounded-lg border border-subtle bg-surface px-3 py-2 text-sm text-muted shadow-lg">
            No invoice matches “{query.trim()}”.
          </div>
        )}
      </div>
    </div>
  );
}

/** Number and customer, on any word typed — capped, so a broad query does not render every invoice. */
function searchInvoices(query: string, invoices: readonly InvoiceSummary[], limit = 50): InvoiceSummary[] {
  const terms = query.toLowerCase().split(/\s+/).filter(Boolean);
  if (terms.length === 0) return invoices.slice(0, limit);

  return invoices
    .filter((invoice) => {
      const haystack = `${invoice.number} ${invoice.customerName ?? ""}`.toLowerCase();
      return terms.every((term) => haystack.includes(term));
    })
    .slice(0, limit);
}

function Row({ label, value, strong = false }: { label: string; value: string; strong?: boolean }) {
  return (
    <div className="flex items-center justify-between">
      <span className={strong ? "font-semibold text-text" : "text-muted"}>{label}</span>
      <span className={`tabular ${strong ? "text-lg font-bold text-text" : "text-text"}`}>{value}</span>
    </div>
  );
}
