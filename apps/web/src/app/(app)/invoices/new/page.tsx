"use client";

/**
 * Raise an invoice — the create screen, Phase 5.
 *
 * This is the line-item prototype (`/prototypes/line-items`) grown up into the real thing. The draft
 * lives here, in the browser, and is posted **whole, once** — the legacy server-session cart is gone
 * (D4). The keyboard contract the prototype proved out is the whole point of it: the entry field is
 * where you start and where you come back to; Enter on a line adds it and drops you into the cell only
 * you can fill; Enter or Escape in any cell returns you to the entry field. A hand that goes to the
 * mouse between lines is the failure the prototype existed to catch.
 *
 * **Item and service are two different documents**, kept separate exactly as the prototype (and the
 * business) keep them: an *item* invoice is raised from the item master, with the price already on the
 * line and its `itemId`/`cost` carried so the save can issue stock and record margin; a *service*
 * invoice is typed, line by line, as the 12,598 legacy lines are. Choosing the kind is a decision made
 * up front and never again — switching it clears the draft, because a typed line has no place on an item
 * invoice and an item line has none on a service one.
 *
 * **One VAT rate, the company's** (the `one-vat-rate-per-document` decision): the server resolves the
 * single rate for the company on the invoice date and is the authority. We fetch that rate once (never
 * per keystroke) so each line shows its VAT %, and the foot reads like a real invoice — subtotal,
 * discount, net, VAT at its rate, total — matching the figure the save returns.
 *
 * Money is held in minor units (fils) and quantities in thousandths, per money.ts, so the figure under
 * the cursor is the integer arithmetic the server re-computes — not a drifting `double`. It is
 * converted back to the major-unit decimals the API expects only at the moment of posting.
 */

import { useCallback, useId, useMemo, useRef, useState, type KeyboardEvent } from "react";
import { useRouter } from "next/navigation";
import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { ArrowLeft, Package, PenLine, Plus, Search, Trash2 } from "lucide-react";
import { ApiError } from "@/lib/api";
import { createInvoice, getInvoiceTaxRate } from "@/lib/invoices";
import { listCompanies, listCustomers, type CustomerSummary } from "@/lib/customers";
import { listItems, type ItemSummary } from "@/lib/items";
import { today } from "@/lib/period";
import {
  extend,
  formatAmount,
  formatQuantity,
  MINOR_UNITS_PER_MAJOR,
  parseAmount,
  parseQuantity,
  percentOf,
  QUANTITY_SCALE,
  roundHalf,
  sum,
  type Minor,
} from "@/lib/money";
import { cn } from "@/lib/cn";
import { PageHeader } from "@/components/shell/app-shell";
import { formatMoney, formatReportDate } from "@/components/reports";
import { Button, Card, ErrorBanner, FadeIn, Input, Select, toast } from "@/components/ui";

/** `item` — every line comes from the master. `service` — every line is typed. Two documents, one engine. */
type DocumentKind = "item" | "service";

interface DraftLine {
  key: string;
  /** The stock item this line is, or null for a typed service line. Kept so the save issues stock. */
  itemId: number | null;
  itemCode: string | null;
  description: string;
  /** Thousandths — see money.ts. 2.5 units is 2500. */
  quantity: number;
  unitPrice: Minor;
  /** 0–100, per line. */
  discountPercent: number;
  /** The line's cost, in minor units — carried from the item master so the save can record margin. */
  cost: Minor | null;
}

const toMinor = (major: number): Minor => roundHalf(major * MINOR_UNITS_PER_MAJOR);

let lineSeq = 0;
const itemLine = (item: ItemSummary): DraftLine => ({
  key: `l${lineSeq++}`,
  itemId: item.id,
  itemCode: item.code,
  description: item.name,
  // One, because one is what it usually is — a quantity already right is a keystroke saved.
  quantity: QUANTITY_SCALE,
  unitPrice: toMinor(item.sellingPrice ?? 0),
  discountPercent: 0,
  cost: item.cost == null ? null : toMinor(item.cost),
});

const serviceLine = (description: string): DraftLine => ({
  key: `l${lineSeq++}`,
  itemId: null,
  itemCode: null,
  description,
  quantity: QUANTITY_SCALE,
  // Zero, and the cursor lands on it: nobody but the person typing knows what this line is worth.
  unitPrice: 0,
  discountPercent: 0,
  cost: null,
});

/**
 * A customer's contacts, out of the one `;`-separated field the master keeps them in. Trimmed, with the
 * blanks a trailing "Ali;" or a stray ";;" leaves dropped. (The master storing several names in one
 * string is the model we would rather replace with real rows — see the note in the customer master.)
 */
function parseContacts(field: string | null | undefined): string[] {
  return (field ?? "")
    .split(";")
    .map((name) => name.trim())
    .filter(Boolean);
}

const lineGross = (l: DraftLine): Minor => extend(l.unitPrice, l.quantity);
const lineNet = (l: DraftLine): Minor => {
  const gross = lineGross(l);
  return gross - percentOf(gross, l.discountPercent);
};

export default function NewInvoicePage() {
  const router = useRouter();
  const companies = useQuery({ queryKey: ["companies"], queryFn: listCompanies });
  const customers = useQuery({ queryKey: ["customers"], queryFn: listCustomers });
  const items = useQuery({ queryKey: ["items"], queryFn: listItems });

  const [kind, setKind] = useState<DocumentKind>("service");
  const [companyId, setCompanyId] = useState("");
  const [customerId, setCustomerId] = useState("");
  const [type, setType] = useState("Credit");
  const [date, setDate] = useState(today);
  const [po, setPo] = useState("");
  const [contact, setContact] = useState("");
  // A discount on the whole document, after the per-line discounts — a discount can be given per line,
  // on the document, or both. Held as the raw string so a half-typed "1." is not fought.
  const [documentDiscount, setDocumentDiscount] = useState("");
  const [lines, setLines] = useState<DraftLine[]>([]);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  // The one rate the whole document carries, resolved by the server for this company on this date.
  // Fetched only when either changes — never while typing lines, which is the prototype's whole promise.
  const taxRate = useQuery({
    queryKey: ["invoice-tax-rate", companyId, date],
    queryFn: () => getInvoiceTaxRate(Number(companyId), date),
    enabled: companyId !== "",
  });
  const rateError = taxRate.error as ApiError | null;
  const ratePercent = taxRate.data?.percentage ?? 0;

  const entry = useRef<HTMLInputElement>(null);
  const quantities = useRef(new Map<string, HTMLInputElement>());
  const prices = useRef(new Map<string, HTMLInputElement>());

  const focusEntry = useCallback(() => entry.current?.focus(), []);

  const patch = (key: string, changes: Partial<DraftLine>) =>
    setLines((current) => current.map((l) => (l.key === key ? { ...l, ...changes } : l)));

  const add = useCallback((line: DraftLine) => {
    setLines((current) => [...current, line]);

    // The new row does not exist in the DOM until React commits it, so the focus move waits for the
    // frame. Straight to the cell the user must fill: an item's quantity, or a typed line's price.
    requestAnimationFrame(() => {
      const cell = (line.itemId === null ? prices : quantities).current.get(line.key);
      cell?.focus();
      cell?.select();
    });
  }, []);

  // Switching document kind clears the draft: an item invoice's lines are not a service invoice's, and
  // carrying them across is exactly the mix the two separate paths exist to prevent.
  const changeKind = (next: DocumentKind) => {
    if (next === kind) return;
    setKind(next);
    setLines([]);
  };

  const docPercent = clampPercent(Number(documentDiscount)) ?? 0;

  // The contact person comes from the customer: the master stores each of a customer's contacts in one
  // `;`-separated field, so we split it into a chosen-from list rather than making the user retype a name
  // the customer record already holds.
  const selectedCustomer = customers.data?.find((c) => String(c.id) === customerId) ?? null;
  const contactOptions = parseContacts(selectedCustomer?.contactPerson);

  const totals = useMemo(() => {
    const subtotal = sum(lines.map(lineGross));
    const lineDiscount = sum(lines.map((l) => percentOf(lineGross(l), l.discountPercent)));
    const linesNet = sum(lines.map(lineNet));

    // The document discount is taken after the line discounts and before VAT — the lines are ex-VAT;
    // VAT lands once, on the document net. This mirrors the server engine, so the preview matches the save.
    const docDiscount = percentOf(linesNet, docPercent);
    const net = linesNet - docDiscount;
    const discount = lineDiscount + docDiscount;
    const vat =
      docPercent > 0
        ? percentOf(net, ratePercent)
        : sum(lines.map((l) => percentOf(lineNet(l), ratePercent)));

    return { subtotal, discount, docDiscount, net, vat, total: net + vat };
  }, [lines, ratePercent, docPercent]);

  const canSubmit =
    companyId !== "" &&
    customerId !== "" &&
    lines.length > 0 &&
    lines.every((l) => l.quantity > 0 && (l.itemId !== null || l.description.trim() !== ""));

  async function submit() {
    setSubmitting(true);
    setError(null);
    try {
      const created = await createInvoice({
        companyId: Number(companyId),
        customerId: Number(customerId),
        type,
        date,
        purchaseOrderNo: po || null,
        contactPerson: contact || null,
        documentDiscountPercent: docPercent,
        // Back to the major-unit decimals the API expects, at the boundary and nowhere else.
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
      toast.success(`Invoice ${created.number} raised — ${formatMoney(created.total)}.`);
      router.push(`/invoices/${created.id}`);
    } catch (e) {
      setError(e as ApiError);
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <FadeIn className="space-y-6">
      <Link
        href="/invoices"
        className="inline-flex items-center gap-1.5 text-sm text-muted transition-colors hover:text-text"
      >
        <ArrowLeft className="size-4" aria-hidden />
        All invoices
      </Link>

      <PageHeader title="New invoice" description="The whole document is posted once — nothing is saved while you type." />

      {error && <ErrorBanner message={error.message} correlationId={error.correlationId} />}

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
            // Default the contact to the customer's first — the common case is a customer with one.
            const picked = customers.data?.find((c) => String(c.id) === id);
            setContact(parseContacts(picked?.contactPerson)[0] ?? "");
          }}
        />

        <Select label="Type" value={type} onChange={(e) => setType(e.target.value)}>
          <option value="Credit">Credit</option>
          <option value="Cash">Cash</option>
        </Select>

        <Input label="Date" type="date" value={date} onChange={(e) => setDate(e.target.value)} />
        <Input label="PO number" value={po} onChange={(e) => setPo(e.target.value)} />

        {/* Chosen from the customer's own contacts when it has them; free text otherwise (and for a
            customer with none on file). */}
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

      <DocumentPicker kind={kind} onChange={changeKind} />

      {rateError && <ErrorBanner message={rateError.message} correlationId={rateError.correlationId} />}

      <Card className="space-y-4 p-5">
        <div className="overflow-x-auto rounded-lg border border-subtle">
          <table className="w-full min-w-3xl text-sm">
            <thead>
              <tr className="border-b border-subtle bg-surface-sunken text-left text-xs uppercase tracking-wide text-muted">
                <th className="px-3 py-2 font-medium">Item</th>
                <th className="w-24 px-3 py-2 text-right font-medium">Qty</th>
                <th className="w-32 px-3 py-2 text-right font-medium">Unit price</th>
                <th className="w-20 px-3 py-2 text-right font-medium">Disc %</th>
                <th className="w-32 px-3 py-2 text-right font-medium">Net</th>
                <th className="w-10 px-2 py-2" />
              </tr>
            </thead>

            <tbody className="divide-y divide-subtle">
              {lines.length === 0 && (
                <tr>
                  <td colSpan={6} className="px-3 py-8 text-center text-muted">
                    {kind === "item"
                      ? "No lines yet. Search the item master below and press Enter."
                      : "No lines yet. Type what you are invoicing for and press Enter."}
                  </td>
                </tr>
              )}

              {lines.map((line) => (
                <Row
                  key={line.key}
                  line={line}
                  patch={(changes) => patch(line.key, changes)}
                  remove={() => {
                    setLines((current) => current.filter((l) => l.key !== line.key));
                    focusEntry();
                  }}
                  registerQuantity={(el) => {
                    if (el) quantities.current.set(line.key, el);
                    else quantities.current.delete(line.key);
                  }}
                  registerPrice={(el) => {
                    if (el) prices.current.set(line.key, el);
                    else prices.current.delete(line.key);
                  }}
                  onDone={focusEntry}
                />
              ))}
            </tbody>
          </table>
        </div>

        {kind === "item" ? (
          <ItemSearch ref={entry} items={items.data ?? []} onPick={(item) => add(itemLine(item))} />
        ) : (
          <ServiceEntry ref={entry} onAdd={(description) => add(serviceLine(description))} />
        )}
      </Card>

      <div className="grid gap-4 sm:grid-cols-2">
        <div />
        <Card className="space-y-2 p-5">
          <Row2 label="Date" value={formatReportDate(date)} />
          <Row2 label="Subtotal" value={formatAmount(totals.subtotal)} />

          {totals.discount - totals.docDiscount > 0 && (
            <Row2 label="Line discounts" value={`− ${formatAmount(totals.discount - totals.docDiscount)}`} />
          )}

          {/* A discount on the whole document — after the line discounts, before VAT. */}
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
            <Row2 label="Document discount" value={`− ${formatAmount(totals.docDiscount)}`} />
          )}

          <Row2 label="Net" value={formatAmount(totals.net)} />
          <Row2
            label={companyId === "" ? "VAT" : taxRate.isPending ? "VAT (…)" : `VAT (${ratePercent}%)`}
            value={formatAmount(totals.vat)}
          />
          <div className="border-t border-subtle pt-2">
            <Row2 label="Total" value={formatAmount(totals.total)} strong />
          </div>

          <Button className="mt-2 w-full" onClick={submit} pending={submitting} disabled={!canSubmit}>
            Raise invoice
          </Button>
        </Card>
      </div>
    </FadeIn>
  );
}

function Row2({ label, value, strong = false }: { label: string; value: string; strong?: boolean }) {
  return (
    <div className="flex items-center justify-between">
      <span className={strong ? "font-semibold text-text" : "text-muted"}>{label}</span>
      <span className={`tabular ${strong ? "text-lg font-bold text-text" : "text-text"}`}>{value}</span>
    </div>
  );
}

// --- The two paths -----------------------------------------------------------------------------

/**
 * Item or service, chosen up front and never again — the same decision the prototype makes, because the
 * business makes it: two different documents, entered by different people for different jobs. Switching
 * clears the draft (handled by the parent), so a mode nobody noticed cannot put a typed line on an item
 * invoice.
 */
function DocumentPicker({ kind, onChange }: { kind: DocumentKind; onChange: (kind: DocumentKind) => void }) {
  const options = [
    {
      value: "service" as const,
      icon: PenLine,
      title: "Service invoice",
      detail: "Lines are typed. This is what the business raises today — every line in the database is one.",
    },
    {
      value: "item" as const,
      icon: Package,
      title: "Item invoice",
      detail: "Lines come from the item master, with the price already on them, and issue stock on save.",
    },
  ];

  return (
    <div className="grid gap-3 sm:grid-cols-2">
      {options.map((option) => {
        const selected = kind === option.value;

        return (
          <button
            key={option.value}
            type="button"
            aria-pressed={selected}
            onClick={() => onChange(option.value)}
            className={cn(
              "flex gap-3 rounded-lg border p-4 text-left transition-colors duration-200 ease-out",
              selected ? "border-primary bg-primary-ghost" : "border-subtle bg-surface hover:border-strong",
            )}
          >
            <option.icon
              className={cn("mt-0.5 size-5 shrink-0", selected ? "text-primary" : "text-muted")}
              aria-hidden
            />
            <span>
              <span className={cn("block font-medium", selected ? "text-primary" : "text-text")}>
                {option.title}
              </span>
              <span className="mt-1 block text-sm text-muted">{option.detail}</span>
            </span>
          </button>
        );
      })}
    </div>
  );
}

// --- One line ----------------------------------------------------------------------------------

function Row({ line, patch, remove, registerQuantity, registerPrice, onDone }: {
  line: DraftLine;
  patch: (changes: Partial<DraftLine>) => void;
  remove: () => void;
  registerQuantity: (el: HTMLInputElement | null) => void;
  registerPrice: (el: HTMLInputElement | null) => void;
  onDone: () => void;
}) {
  return (
    <tr className="bg-surface align-top">
      <td className="px-3 py-1.5">
        <TextCell
          value={line.description}
          onCommit={(description) => patch({ description })}
          onDone={onDone}
          label={`Description of line ${line.description}`}
        />
        <span className="mt-0.5 block px-2 font-mono text-xs text-muted">
          {line.itemCode ?? <span className="font-sans">Typed line — no item</span>}
        </span>
      </td>

      <td className="px-3 py-1.5">
        <NumberCell
          ref={registerQuantity}
          value={formatQuantity(line.quantity)}
          parse={parseQuantity}
          onCommit={(quantity) => patch({ quantity })}
          onDone={onDone}
          label={`Quantity of ${line.description}`}
        />
      </td>

      <td className="px-3 py-1.5">
        <NumberCell
          ref={registerPrice}
          value={formatAmount(line.unitPrice)}
          parse={parseAmount}
          onCommit={(unitPrice) => patch({ unitPrice })}
          onDone={onDone}
          label={`Unit price of ${line.description}`}
        />
      </td>

      <td className="px-3 py-1.5">
        <NumberCell
          value={line.discountPercent.toString()}
          parse={(raw) => (raw === "" ? null : clampPercent(Number(raw)))}
          onCommit={(discountPercent) => patch({ discountPercent })}
          onDone={onDone}
          label={`Discount on ${line.description}`}
        />
      </td>

      {/* Lines are ex-VAT: the net is before tax, and VAT lands once, on the document (the foot). */}
      <td className="px-3 py-1.5 text-right tabular text-text">{formatAmount(lineNet(line))}</td>

      <td className="px-2 py-1.5">
        <button
          type="button"
          aria-label={`Remove ${line.description}`}
          onClick={remove}
          className={cn(
            "rounded p-1.5 text-muted transition-colors duration-150",
            "hover:bg-danger-subtle hover:text-danger",
            "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring/25",
          )}
        >
          <Trash2 className="size-4" />
        </button>
      </td>
    </tr>
  );
}

/**
 * A numeric cell that tolerates being typed into — the value halfway through ("1.", "12.") is not a
 * number, so the raw string is local state and the draft is only told when it parses. Enter or Escape
 * gets back to the entry field, ready for the next line.
 */
function NumberCell({ value, parse, onCommit, onDone, label, ref }: {
  value: string;
  parse: (raw: string) => number | null;
  onCommit: (parsed: number) => void;
  onDone: () => void;
  label: string;
  ref?: React.Ref<HTMLInputElement>;
}) {
  const [raw, setRaw] = useState<string | null>(null);

  const onKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    if (event.key === "Enter" || event.key === "Escape") {
      event.preventDefault();
      setRaw(null);
      onDone();
    }
  };

  return (
    <input
      ref={ref}
      aria-label={label}
      inputMode="decimal"
      value={raw ?? value}
      onChange={(event) => {
        setRaw(event.target.value);
        const parsed = parse(event.target.value.trim());
        if (parsed !== null) onCommit(parsed);
      }}
      onFocus={(event) => event.target.select()}
      onBlur={() => setRaw(null)}
      onKeyDown={onKeyDown}
      className={cn(
        "w-full rounded border border-transparent bg-transparent px-2 py-1 text-right tabular text-text",
        "transition-colors duration-150 hover:border-subtle",
        "focus:border-primary focus:bg-canvas focus:outline-none",
      )}
    />
  );
}

const clampPercent = (value: number) =>
  Number.isFinite(value) ? Math.min(100, Math.max(0, value)) : null;

/** The description, in place. Same keyboard contract as the numeric cells: Enter or Escape gets out. */
function TextCell({ value, onCommit, onDone, label }: {
  value: string;
  onCommit: (value: string) => void;
  onDone: () => void;
  label: string;
}) {
  return (
    <input
      aria-label={label}
      value={value}
      onChange={(event) => onCommit(event.target.value)}
      onKeyDown={(event) => {
        if (event.key === "Enter" || event.key === "Escape") {
          event.preventDefault();
          onDone();
        }
      }}
      className={cn(
        "w-full rounded border border-transparent bg-transparent px-2 py-1 text-text",
        "transition-colors duration-150 hover:border-subtle",
        "focus:border-primary focus:bg-canvas focus:outline-none",
      )}
    />
  );
}

// --- Entry fields --------------------------------------------------------------------------------

/**
 * The item document's entry field: type, arrow, Enter. Catalogue only.
 *
 * It searches the item master (`/api/items`) and cannot free-type — an item invoice whose lines are not
 * items is the thing the two separate paths exist to prevent. An item the master does not have is added
 * on the Items screen first; here it is picked.
 */
function ItemSearch({ items, onPick, ref }: {
  items: readonly ItemSummary[];
  onPick: (item: ItemSummary) => void;
  ref?: React.Ref<HTMLInputElement>;
}) {
  const [query, setQuery] = useState("");
  const [highlighted, setHighlighted] = useState(0);

  const results = useMemo(() => searchItems(query, items), [query, items]);
  const active = results[Math.min(highlighted, results.length - 1)];
  const typed = query.trim();

  const reset = () => {
    setQuery("");
    setHighlighted(0);
  };

  const onKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    switch (event.key) {
      case "ArrowDown":
        event.preventDefault();
        setHighlighted((current) => Math.min(current + 1, results.length - 1));
        break;
      case "ArrowUp":
        event.preventDefault();
        setHighlighted((current) => Math.max(current - 1, 0));
        break;
      case "Enter":
        event.preventDefault();
        if (active) {
          onPick(active);
          reset();
        }
        break;
      case "Escape":
        reset();
        break;
    }
  };

  return (
    <div className="relative">
      <div className="flex items-center gap-2 rounded-lg border border-subtle bg-surface px-3 focus-within:border-primary">
        <Search className="size-4 shrink-0 text-muted" aria-hidden />
        <input
          ref={ref}
          value={query}
          autoFocus
          aria-label="Add an item"
          placeholder="Add an item — type a code or a description, then Enter"
          onChange={(event) => {
            setQuery(event.target.value);
            setHighlighted(0);
          }}
          onKeyDown={onKeyDown}
          className="w-full bg-transparent py-2.5 text-sm text-text placeholder:text-muted focus:outline-none"
        />
        {typed && results.length === 0 && (
          <span className="shrink-0 text-xs text-muted">Not in the item master — add it on the Items screen</span>
        )}
      </div>

      {results.length > 0 && (
        <ul
          role="listbox"
          className="absolute bottom-full z-10 mb-1 w-full overflow-hidden rounded-lg border border-subtle bg-surface shadow-lg"
        >
          {results.map((item, index) => (
            <li key={item.id}>
              <button
                type="button"
                role="option"
                aria-selected={item === active}
                // Mouse down, not click: click fires after blur, by which point the field has lost
                // focus and the list has closed under the pointer.
                onMouseDown={(event) => {
                  event.preventDefault();
                  onPick(item);
                  reset();
                }}
                onMouseEnter={() => setHighlighted(index)}
                className={cn(
                  "flex w-full items-center gap-3 px-3 py-2 text-left text-sm",
                  item === active ? "bg-primary-ghost text-primary" : "text-text",
                )}
              >
                <span className="w-36 shrink-0 font-mono text-xs text-muted">{item.code}</span>
                <span className="min-w-0 flex-1 truncate">{item.name}</span>
                <Stock item={item} />
                <span className="tabular text-muted">{formatAmount(toMinor(item.sellingPrice ?? 0))}</span>
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

/**
 * The service document's entry field: type it, press Enter, keep going.
 *
 * This is the document the business actually raises — every legacy invoice line is a typed line — so it
 * gets the same grid, totals and keyboard as the picker. The only thing it does not get is a catalogue:
 * the line is whatever the job was.
 */
function ServiceEntry({ onAdd, ref }: {
  onAdd: (description: string) => void;
  ref?: React.Ref<HTMLInputElement>;
}) {
  const [description, setDescription] = useState("");
  const typed = description.trim();

  return (
    <div className="flex items-center gap-2 rounded-lg border border-subtle bg-surface px-3 focus-within:border-primary">
      <Plus className="size-4 shrink-0 text-muted" aria-hidden />
      <input
        ref={ref}
        value={description}
        autoFocus
        aria-label="Add a line"
        placeholder="What are you invoicing for? — e.g. Supply and fix 3 network points, 2nd floor"
        onChange={(event) => setDescription(event.target.value)}
        onKeyDown={(event) => {
          if (event.key === "Enter" && typed) {
            event.preventDefault();
            onAdd(typed);
            setDescription("");
          }
          if (event.key === "Escape") setDescription("");
        }}
        className="w-full bg-transparent py-2.5 text-sm text-text placeholder:text-muted focus:outline-none"
      />
      {typed && <span className="shrink-0 text-xs text-muted">Enter to add</span>}
    </div>
  );
}

/**
 * Matches on code and name, on any word typed — substring, not prefix, because staff know an item as
 * "24 port poe", never as "SW-24P-POE".
 */
function searchItems(query: string, items: readonly ItemSummary[], limit = 8): ItemSummary[] {
  const terms = query.toLowerCase().split(/\s+/).filter(Boolean);
  if (terms.length === 0) return [];

  return items
    .filter((item) => {
      const haystack = `${item.code} ${item.name}`.toLowerCase();
      return terms.every((term) => haystack.includes(term));
    })
    .slice(0, limit);
}

/** Stock at a glance while picking — what `getAllItemsStk` does today, and staff rely on it. */
function Stock({ item }: { item: ItemSummary }) {
  if (item.stockBalance <= 0) return <span className="text-xs text-muted">out of stock</span>;

  return (
    <span className={cn("text-xs", item.belowReorder ? "text-warning-text" : "text-muted")}>
      {formatQuantity(roundHalf(item.stockBalance * QUANTITY_SCALE))} in stock
    </span>
  );
}

// --- Customer picker -----------------------------------------------------------------------------

/**
 * The customer field, searchable — because the list is long and a native dropdown of hundreds is a
 * dropdown nobody can find their customer in. Type a code or any word in the name; arrow and Enter, or
 * click, to choose. Same keyboard as the item picker, so there is one contract to learn, not two.
 */
function CustomerCombobox({ customers, value, onChange }: {
  customers: readonly CustomerSummary[];
  value: string;
  onChange: (id: string) => void;
}) {
  const listboxId = useId();
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState("");
  const [highlighted, setHighlighted] = useState(0);

  const selected = customers.find((c) => String(c.id) === value) ?? null;
  const results = useMemo(() => searchCustomers(query, customers), [query, customers]);
  const active = results[Math.min(highlighted, results.length - 1)];

  const choose = (customer: CustomerSummary) => {
    onChange(String(customer.id));
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

  // Closed, the field reads as the chosen customer; open, it is a search box the user is typing into.
  const shownValue = open ? query : selected ? `${selected.code} — ${selected.name}` : "";

  return (
    <div className="space-y-1.5">
      <label className="block text-sm font-medium text-text">Customer</label>

      <div className="relative">
        <div className="flex items-center gap-2 rounded-md border border-subtle bg-surface px-3 focus-within:border-strong focus-within:ring-2 focus-within:ring-ring/25">
          <Search className="size-4 shrink-0 text-muted" aria-hidden />
          <input
            role="combobox"
            aria-expanded={open}
            aria-controls={listboxId}
            aria-autocomplete="list"
            value={shownValue}
            placeholder="Search customers…"
            onFocus={() => setOpen(true)}
            onChange={(event) => {
              setQuery(event.target.value);
              setOpen(true);
              setHighlighted(0);
            }}
            onKeyDown={onKeyDown}
            // A click outside closes it; the short delay lets an option's mousedown land first.
            onBlur={() => setTimeout(() => setOpen(false), 120)}
            className="w-full bg-transparent py-2 text-sm text-text placeholder:text-muted focus:outline-none"
          />
        </div>

        {open && results.length > 0 && (
          <ul
            role="listbox"
            id={listboxId}
            className="absolute top-full z-10 mt-1 max-h-64 w-full overflow-auto rounded-lg border border-subtle bg-surface shadow-lg"
          >
            {results.map((customer, index) => (
              <li key={customer.id}>
                <button
                  type="button"
                  role="option"
                  aria-selected={customer === active}
                  onMouseDown={(event) => {
                    event.preventDefault();
                    choose(customer);
                  }}
                  onMouseEnter={() => setHighlighted(index)}
                  className={cn(
                    "flex w-full items-center gap-3 px-3 py-2 text-left text-sm",
                    customer === active ? "bg-primary-ghost text-primary" : "text-text",
                  )}
                >
                  <span className="w-20 shrink-0 font-mono text-xs text-muted">{customer.code}</span>
                  <span className="min-w-0 flex-1 truncate">{customer.name}</span>
                </button>
              </li>
            ))}
          </ul>
        )}

        {open && query.trim() !== "" && results.length === 0 && (
          <div className="absolute top-full z-10 mt-1 w-full rounded-lg border border-subtle bg-surface px-3 py-2 text-sm text-muted shadow-lg">
            No customer matches “{query.trim()}”.
          </div>
        )}
      </div>
    </div>
  );
}

/** Code and name, on any word typed — capped, so a broad query does not render the whole book. */
function searchCustomers(query: string, customers: readonly CustomerSummary[], limit = 50): CustomerSummary[] {
  const terms = query.toLowerCase().split(/\s+/).filter(Boolean);
  if (terms.length === 0) return customers.slice(0, limit);

  return customers
    .filter((customer) => {
      const haystack = `${customer.code} ${customer.name}`.toLowerCase();
      return terms.every((term) => haystack.includes(term));
    })
    .slice(0, limit);
}
