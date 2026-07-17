"use client";

/**
 * The shared line-item draft editor — the browser-held document draft that both invoices and quotations
 * are built from (Phase 5). One editor, because a document is a document: the legacy app's item/service
 * split is a per-line distinction (an item line references the master and carries a cost; a service line
 * is typed), not a different screen. The draft lives in the browser and is posted whole, once — the
 * server-session cart is gone (D4).
 *
 * The keyboard contract is the whole point (proven by the `/prototypes/line-items` prototype): the entry
 * field is where you start and return to; Enter on a line adds it and drops you into the one cell only
 * you can fill; Enter or Escape in any cell returns you to the entry field. A hand that goes to the mouse
 * between lines is the failure this exists to prevent.
 *
 * Money is held in minor units (fils) and quantities in thousandths (money.ts), so the figure under the
 * cursor is the integer arithmetic the server re-computes — not a drifting double. It is converted back
 * to major-unit decimals only at the moment of posting, by the page.
 */

import { useCallback, useId, useMemo, useRef, useState, type KeyboardEvent } from "react";
import { Package, PenLine, Plus, Search, Trash2 } from "lucide-react";
import type { CustomerSummary } from "@/lib/customers";
import type { ItemSummary } from "@/lib/items";
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
import { Card } from "@/components/ui";

/** `item` — every line comes from the master. `service` — every line is typed. Two documents, one engine. */
export type DocumentKind = "item" | "service";

export interface DraftLine {
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

export const toMinor = (major: number): Minor => roundHalf(major * MINOR_UNITS_PER_MAJOR);

let lineSeq = 0;
export const itemLine = (item: ItemSummary): DraftLine => ({
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

export const serviceLine = (description: string): DraftLine => ({
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
 * A customer's contacts, out of the one `;`-separated field the master keeps them in. Trimmed, blanks
 * dropped. Retained as the fallback for customers whose structured contacts have not been backfilled yet.
 */
export function parseContacts(field: string | null | undefined): string[] {
  return (field ?? "")
    .split(";")
    .map((name) => name.trim())
    .filter(Boolean);
}

/**
 * The pickable contact names for a customer (Phase 6, slice 4): the structured `customer_contacts` rows
 * when it has them, otherwise the legacy `;`-separated string parsed the old way — so a customer that has
 * not been backfilled yet still offers its contacts.
 */
export function customerContactNames(customer: { contacts?: readonly { name?: string | null; usage?: string | null }[] | null; contactPerson?: string | null } | null | undefined): string[] {
  if (!customer) return [];
  // Only document contacts are offered as a document's contact person — notification-only contacts are not.
  const structured = (customer.contacts ?? [])
    .filter((c) => c.usage !== "NotificationsOnly")
    .map((c) => c.name?.trim())
    .filter((n): n is string => Boolean(n));
  const anyStructured = (customer.contacts ?? []).some((c) => (c.name ?? "").trim());
  return anyStructured ? structured : parseContacts(customer.contactPerson);
}

export const clampPercent = (value: number) =>
  Number.isFinite(value) ? Math.min(100, Math.max(0, value)) : null;

const lineGross = (l: DraftLine): Minor => extend(l.unitPrice, l.quantity);
const lineNet = (l: DraftLine): Minor => {
  const gross = lineGross(l);
  return gross - percentOf(gross, l.discountPercent);
};

/** The document foot, mirroring the server tax engine so the preview matches the figure the save returns. */
export function useDraftTotals(lines: DraftLine[], ratePercent: number, docPercent: number) {
  return useMemo(() => {
    const subtotal = sum(lines.map(lineGross));
    const lineDiscount = sum(lines.map((l) => percentOf(lineGross(l), l.discountPercent)));
    const linesNet = sum(lines.map(lineNet));

    // The document discount is taken after the line discounts and before VAT — the lines are ex-VAT;
    // VAT lands once, on the document net. This mirrors the server engine.
    const docDiscount = percentOf(linesNet, docPercent);
    const net = linesNet - docDiscount;
    const discount = lineDiscount + docDiscount;
    const vat =
      docPercent > 0
        ? percentOf(net, ratePercent)
        : sum(lines.map((l) => percentOf(lineNet(l), ratePercent)));

    return { subtotal, discount, docDiscount, net, vat, total: net + vat };
  }, [lines, ratePercent, docPercent]);
}

/** True when every line is complete enough to post: a positive quantity and an item or a description. */
export const linesArePostable = (lines: DraftLine[]) =>
  lines.length > 0 &&
  lines.every((l) => l.quantity > 0 && (l.itemId !== null || l.description.trim() !== ""));

// --- The editor ----------------------------------------------------------------------------------

/**
 * Picker + table + entry field, in one card. The parent owns the `lines` and `kind` state (it needs the
 * lines for the foot and the post); this owns the focus choreography between the entry field and the
 * cells. Switching kind clears the draft — a typed line has no place on an item document and vice versa.
 */
export function LineDraftEditor({
  kind,
  onKindChange,
  lines,
  onLinesChange,
  items,
}: {
  kind: DocumentKind;
  onKindChange: (kind: DocumentKind) => void;
  lines: DraftLine[];
  onLinesChange: (updater: (current: DraftLine[]) => DraftLine[]) => void;
  items: readonly ItemSummary[];
}) {
  const entry = useRef<HTMLInputElement>(null);
  const quantities = useRef(new Map<string, HTMLInputElement>());
  const prices = useRef(new Map<string, HTMLInputElement>());

  const focusEntry = useCallback(() => entry.current?.focus(), []);

  const patch = (key: string, changes: Partial<DraftLine>) =>
    onLinesChange((current) => current.map((l) => (l.key === key ? { ...l, ...changes } : l)));

  const add = useCallback(
    (line: DraftLine) => {
      onLinesChange((current) => [...current, line]);

      // The new row does not exist in the DOM until React commits it, so the focus move waits for the
      // frame. Straight to the cell the user must fill: an item's quantity, or a typed line's price.
      requestAnimationFrame(() => {
        const cell = (line.itemId === null ? prices : quantities).current.get(line.key);
        cell?.focus();
        cell?.select();
      });
    },
    [onLinesChange],
  );

  const changeKind = (next: DocumentKind) => {
    if (next === kind) return;
    onKindChange(next);
    onLinesChange(() => []);
  };

  return (
    <>
      <DocumentPicker kind={kind} onChange={changeKind} />

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
                      : "No lines yet. Type what you are billing for and press Enter."}
                  </td>
                </tr>
              )}

              {lines.map((line) => (
                <Row
                  key={line.key}
                  line={line}
                  patch={(changes) => patch(line.key, changes)}
                  remove={() => {
                    onLinesChange((current) => current.filter((l) => l.key !== line.key));
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
          <ItemSearch ref={entry} items={items} onPick={(item) => add(itemLine(item))} />
        ) : (
          <ServiceEntry ref={entry} onAdd={(description) => add(serviceLine(description))} />
        )}
      </Card>
    </>
  );
}

/**
 * Item or service, chosen up front and never again — the same decision the business makes: two different
 * documents, entered by different people for different jobs. Switching clears the draft (handled by the
 * editor), so a mode nobody noticed cannot put a typed line on an item document.
 */
function DocumentPicker({ kind, onChange }: { kind: DocumentKind; onChange: (kind: DocumentKind) => void }) {
  const options = [
    {
      value: "service" as const,
      icon: PenLine,
      title: "Service document",
      detail: "Lines are typed. This is what the business raises today — every legacy line is one.",
    },
    {
      value: "item" as const,
      icon: Package,
      title: "Item document",
      detail: "Lines come from the item master, with the price already on them.",
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
        const next = event.target.value;
        if (!/^-?\d*\.?\d*$/.test(next)) return; // a value cell holds only a number
        setRaw(next);
        const parsed = parse(next.trim());
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

/**
 * The item document's entry field: type, arrow, Enter. Catalogue only — a document whose lines are not
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
                // Mouse down, not click: click fires after blur, by which point the field has lost focus
                // and the list has closed under the pointer.
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
 * The service document's entry field: type it, press Enter, keep going. Every legacy line is a typed
 * line, so it gets the same grid, totals and keyboard as the picker — it just has no catalogue.
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
        placeholder="What is this line? — e.g. Supply and fix 3 network points, 2nd floor"
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

/**
 * The customer field, searchable — because the list is long and a native dropdown of hundreds is one
 * nobody can find their customer in. Type a code or any word in the name; arrow and Enter, or click.
 */
export function CustomerCombobox({ customers, value, onChange }: {
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
