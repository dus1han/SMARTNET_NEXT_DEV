"use client";

import { Plus, Search, Trash2 } from "lucide-react";
import {
  useCallback,
  useId,
  useRef,
  useState,
  type KeyboardEvent,
  type ReactNode,
} from "react";
import { cn } from "@/lib/cn";
import {
  formatAmount,
  formatQuantity,
  parseAmount,
  parseQuantity,
  QUANTITY_SCALE,
} from "@/lib/money";
import { Badge, Button, Dialog, Input } from "@/components/ui";
import { CATALOGUE, searchCatalogue, type CatalogueItem } from "./catalogue";
import { documentTotals, lineTotals, type DraftAction, type DraftLine } from "./draft";

/**
 * THE LINE-ITEM EDITOR.
 *
 * Risk work, deliberately built three phases early. The parent plan's own risk register: *"The cart
 * rewrite (Phase 5) changes how staff enter documents — prototype the line-item editor in Phase 2
 * and put it in front of a real user before Phase 5."* Finding out in Phase 5 that the new entry
 * flow is slower than the old one costs five weeks. Finding out now costs two days.
 *
 * **What it is testing is the keyboard, not the pixels.** The person this is for types invoices all
 * day and does not look at the screen while they do it. Today each line costs them a server round
 * trip (`addtoCart`) and a full grid redraw (`cartLoad`); the win here is that nothing is sent while
 * they type at all. That win is only real if the hands never leave the keyboard, which is why the
 * focus choreography below is the design rather than a detail of it:
 *
 *   - the search field is always where you start and always where you come back to;
 *   - Enter on a search result adds the line and drops the cursor into its quantity, selected;
 *   - Enter in any cell returns to the search field, ready for the next line;
 *   - Escape gets you out of anything, back to the search field.
 *
 * If a real user's hand goes to the mouse between lines, this prototype has failed and it is better
 * to know that now.
 */
export interface LineItemsProps {
  lines: DraftLine[];
  dispatch: (action: DraftAction) => void;

  /**
   * Which document is being typed.
   *
   * The business keeps these two paths **separate**, and that is a product decision, not an accident
   * of the legacy code: an *item* invoice is raised from the item master, a *service* invoice is
   * typed, and they are different documents entered by different people for different jobs. One grid
   * with a mode the user has to notice is how a service line ends up on an item invoice.
   *
   * So the paths are separate and the engine is not. Everything below — the grid, the totals, the
   * tax summary, the draft, the payload — is shared; the only difference is where a line may come
   * from. That is the whole of ISSUES D3's four-controllers-per-document collapse, in one prop.
   */
  kind: DocumentKind;

  /** Prototype instrumentation: the caller counts what it likes. */
  onLineAdded?: () => void;
}

/** `item` — every line comes from the catalogue. `service` — every line is typed. */
export type DocumentKind = "item" | "service";

/** A line picked from the catalogue: an *item* document's line. Its price is known. */
const fromCatalogue = (item: CatalogueItem): Omit<DraftLine, "id"> => ({
  itemCode: item.code,
  description: item.name,

  // One, because one is what it usually is, and a quantity that is already right is a keystroke the
  // user does not have to spend.
  quantity: QUANTITY_SCALE,
  unitPrice: item.unitPrice,
  discountPercent: 0,
  taxRate: item.taxRate,
});

/**
 * A line that is not in the catalogue: a *service* document's line.
 *
 * Half the documents this business raises are these — "supply and fix 3 points", "site survey" — and
 * they have no item and never will. A line editor that can only pick from a catalogue cannot type a
 * service invoice at all, which is why the legacy app has a second set of screens for them. Here it
 * is the same grid: type something the catalogue does not have, press Enter, and keep going.
 */
const freeText = (description: string): Omit<DraftLine, "id"> => ({
  itemCode: null,
  description,
  quantity: QUANTITY_SCALE,

  // Zero, and the cursor lands on it: nobody but the person typing knows what this line is worth.
  unitPrice: 0,
  discountPercent: 0,
  taxRate: STANDARD_VAT,
});

/** The default for a typed line. Per line, and overridable — a zero-rated service exists. */
const STANDARD_VAT = 5;

export function LineItems({ lines, dispatch, kind, onLineAdded }: LineItemsProps) {
  const search = useRef<HTMLInputElement>(null);
  const quantities = useRef(new Map<string, HTMLInputElement>());
  const prices = useRef(new Map<string, HTMLInputElement>());

  /**
   * The item master, as this browser currently knows it.
   *
   * In the prototype the new item lives only here, for the length of the session. In Phase 3 the
   * dialog below posts to `/api/items` and the list is a query — but the *flow* is the one being
   * tested, and it is the flow the business asked for: an item that is not in the master gets added
   * to the master, and then it is on the invoice. It does not become a typed line, because a typed
   * line on an item invoice is precisely what the two separate paths exist to prevent.
   */
  const [catalogue, setCatalogue] = useState<CatalogueItem[]>(CATALOGUE);

  const focusSearch = useCallback(() => search.current?.focus(), []);

  const add = useCallback(
    (line: Omit<DraftLine, "id">) => {
      const id = crypto.randomUUID();

      dispatch({ type: "add", id, line });

      onLineAdded?.();

      // The new row does not exist in the DOM until React has committed it, so the focus move waits
      // for the frame. requestAnimationFrame, not setTimeout(0): it is the paint we are waiting on.
      requestAnimationFrame(() => {
        // Straight to whichever cell the user must fill in next: the quantity of a catalogue item,
        // whose price we already know — or the price of a typed line, which nobody but them knows.
        const cell = (line.itemCode === null ? prices : quantities).current.get(id);

        cell?.focus();

        // Selected, not appended to: the next thing typed is a number, and "1" followed by "2" must
        // mean 2, never 12.
        cell?.select();
      });
    },
    [dispatch, onLineAdded],
  );

  const totals = documentTotals(lines);

  return (
    <div className="space-y-4">
      <div className="overflow-x-auto rounded-lg border border-subtle">
        <table className="w-full min-w-3xl text-sm">
          <thead>
            <tr className="border-b border-subtle bg-surface-sunken text-left text-xs uppercase tracking-wide text-muted">
              <th className="px-3 py-2 font-medium">Item</th>
              <th className="w-24 px-3 py-2 text-right font-medium">Qty</th>
              <th className="w-32 px-3 py-2 text-right font-medium">Unit price</th>
              <th className="w-20 px-3 py-2 text-right font-medium">Disc %</th>
              <th className="w-20 px-3 py-2 text-right font-medium">VAT</th>
              <th className="w-32 px-3 py-2 text-right font-medium">Total</th>
              <th className="w-10 px-2 py-2" />
            </tr>
          </thead>

          <tbody className="divide-y divide-subtle">
            {lines.length === 0 && (
              <tr>
                <td colSpan={7} className="px-3 py-8 text-center text-muted">
                  {kind === "item"
                    ? "No lines yet. Search the item master below and press Enter."
                    : "No lines yet. Type what you are selling and press Enter."}
                </td>
              </tr>
            )}

            {lines.map((line) => (
              <Row
                key={line.id}
                line={line}
                dispatch={dispatch}
                // Inline, not a helper: passing the ref object to a function during render is a
                // React Compiler error, and the callback itself only ever runs after commit.
                registerQuantity={(element) => {
                  if (element) quantities.current.set(line.id, element);
                  else quantities.current.delete(line.id);
                }}
                registerPrice={(element) => {
                  if (element) prices.current.set(line.id, element);
                  else prices.current.delete(line.id);
                }}
                onDone={focusSearch}
              />
            ))}
          </tbody>

          {lines.length > 0 && (
            <tfoot className="border-t-2 border-strong">
              <tr>
                <td colSpan={5} className="px-3 py-2 text-right text-muted">
                  Net
                </td>
                <td className="px-3 py-2 text-right tabular">{formatAmount(totals.net)}</td>
                <td />
              </tr>

              {/* The VAT summary, grouped by rate. The legacy document cannot print this: `vatper`
                  is one display string for the whole invoice (ISSUES B5), so a zero-rated line
                  beside a standard-rated one is not representable — it is simply wrong. */}
              {totals.taxByRate.map((group) => (
                <tr key={group.rate}>
                  <td colSpan={5} className="px-3 py-2 text-right text-muted">
                    VAT at {group.rate}% <span className="text-xs">on {formatAmount(group.net)}</span>
                  </td>
                  <td className="px-3 py-2 text-right tabular">{formatAmount(group.tax)}</td>
                  <td />
                </tr>
              ))}

              <tr className="border-t border-subtle">
                <td colSpan={5} className="px-3 py-2.5 text-right font-medium text-text">
                  Total
                </td>
                <td className="px-3 py-2.5 text-right font-semibold tabular text-text">
                  {formatAmount(totals.total)}
                </td>
                <td />
              </tr>
            </tfoot>
          )}
        </table>
      </div>

      {kind === "item" ? (
        <ItemSearch
          ref={search}
          catalogue={catalogue}
          onPick={(item) => add(fromCatalogue(item))}
          onCreate={(item) => {
            setCatalogue((current) => [item, ...current]);
            add(fromCatalogue(item));
          }}
        />
      ) : (
        <ServiceEntry ref={search} onAdd={(description) => add(freeText(description))} />
      )}
    </div>
  );
}

// --- One line ------------------------------------------------------------------------------

function Row({ line, dispatch, registerQuantity, registerPrice, onDone }: {
  line: DraftLine;
  dispatch: (action: DraftAction) => void;
  registerQuantity: (element: HTMLInputElement | null) => void;
  registerPrice: (element: HTMLInputElement | null) => void;
  onDone: () => void;
}) {
  const totals = lineTotals(line);

  const patch = (patch: Partial<DraftLine>) => dispatch({ type: "update", id: line.id, patch });

  return (
    <tr className="bg-surface">
      <td className="px-3 py-1.5">
        {/* Editable on both kinds of line. The legacy item screen copies the item's name into the
            line so it can be typed over — "Cat6 cable" becomes "Cat6 cable (customer's own drum)" —
            and the item it came from stays recorded either way. That is the bit the old save
            throws away. */}
        <TextCell
          value={line.description}
          onCommit={(description) => patch({ description })}
          onDone={onDone}
          label={`Description of line ${line.description}`}
        />

        <span className="mt-0.5 block px-2 font-mono text-xs text-muted">
          {line.itemCode ?? <span className="not-italic font-sans">Typed line — no item</span>}
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

      <td className="px-3 py-1.5 text-right">
        <Badge tone={line.taxRate === 0 ? "neutral" : "success"}>{line.taxRate}%</Badge>
      </td>

      <td className="px-3 py-1.5 text-right tabular text-text">{formatAmount(totals.total)}</td>

      <td className="px-2 py-1.5">
        <button
          type="button"
          aria-label={`Remove ${line.description}`}
          onClick={() => {
            dispatch({ type: "remove", id: line.id });
            onDone();
          }}
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
 * A numeric cell that tolerates being typed into.
 *
 * The value the user is halfway through typing ("1." , "-", "12.") is not a number, and a cell that
 * refuses to hold it forces them to type right-to-left around their own input. So the raw string is
 * local state, and the draft is only told about it when it parses. On blur the cell re-formats from
 * the draft, which is the moment "1.5" becomes "1.5" and "abc" quietly becomes what it was.
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
  const id = useId();

  const onKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    if (event.key === "Enter" || event.key === "Escape") {
      // Back to the search field: the next thing this person does is type the next line, and every
      // Tab they have to press to get back there is a keystroke the old screen did not charge them.
      event.preventDefault();
      setRaw(null);
      onDone();
    }
  };

  return (
    <input
      ref={ref}
      id={id}
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

// --- The search field ----------------------------------------------------------------------

/**
 * The item document's entry field: type, arrow, Enter. Catalogue only.
 *
 * It cannot free-type, on purpose. An item invoice whose lines are not items is the thing the two
 * separate paths exist to prevent, and a picker that quietly accepts anything typed into it will one
 * day accept "Cat6 cabel" as a product the business sells.
 *
 * **So an item that is not in the master gets added to the master** — that is the business's own
 * answer, and it is the only one that keeps the catalogue worth having. The dialog is right here, in
 * the flow, because an "add item" that means *leave this invoice, go to another screen, come back and
 * start again* is an "add item" nobody uses: they raise a service invoice instead, and the item master
 * dies. Which is precisely what has happened.
 *
 * It searches a local catalogue because there is no items endpoint until Phase 3 — see catalogue.ts.
 * The props do not change when there is one.
 */
function ItemSearch({ catalogue, onPick, onCreate, ref }: {
  catalogue: readonly CatalogueItem[];
  onPick: (item: CatalogueItem) => void;
  onCreate: (item: CatalogueItem) => void;
  ref?: React.Ref<HTMLInputElement>;
}) {
  const [query, setQuery] = useState("");
  const [highlighted, setHighlighted] = useState(0);

  /** The name the user typed and could not find. Non-null while the "new item" dialog is open. */
  const [creating, setCreating] = useState<string | null>(null);

  const results = searchCatalogue(query, catalogue);
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
        } else if (typed) {
          // Not in the master. Enter takes you straight into adding it — without leaving the invoice,
          // and with what you already typed carried in as its name.
          setCreating(typed);
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
          <span className="shrink-0 text-xs text-muted">
            Not in the item master — Enter to add it
          </span>
        )}
      </div>

      <NewItemDialog
        name={creating}
        onClose={() => setCreating(null)}
        onCreate={(item) => {
          onCreate(item);
          setCreating(null);
          reset();
        }}
      />

      {results.length > 0 && (
        <ul
          role="listbox"
          className="absolute bottom-full z-10 mb-1 w-full overflow-hidden rounded-lg border border-subtle bg-surface shadow-lg"
        >
          {results.map((item, index) => (
            <li key={item.code}>
              <button
                type="button"
                role="option"
                aria-selected={item === active}
                // Mouse down, not click: click fires after blur, and by then the search field has
                // lost focus and the list has closed underneath the pointer.
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
                <span className="tabular text-muted">{formatAmount(item.unitPrice)}</span>
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

/**
 * Adds an item to the master, from inside the invoice.
 *
 * Four fields, because four is what a line needs: what it is called, what it costs the customer, what
 * VAT it carries, and a code to find it by next time. Anything else the item master eventually holds
 * — reorder level, cost, supplier — is not needed to get this invoice out of the door, and asking for
 * it here is how the dialog becomes the thing people avoid.
 */
function NewItemDialog({ name, onClose, onCreate }: {
  name: string | null;
  onClose: () => void;
  onCreate: (item: CatalogueItem) => void;
}) {
  const [code, setCode] = useState("");
  const [itemName, setItemName] = useState("");
  const [price, setPrice] = useState("");
  const [taxRate, setTaxRate] = useState(STANDARD_VAT);
  const [loaded, setLoaded] = useState<string | null>(null);

  // Carry the words they already typed in as the name, during render rather than in an effect.
  if (name !== null && loaded !== name) {
    setItemName(name);
    setCode("");
    setPrice("");
    setTaxRate(STANDARD_VAT);
    setLoaded(name);
  }

  const unitPrice = parseAmount(price);
  const valid = code.trim().length > 0 && itemName.trim().length > 0 && unitPrice !== null;

  return (
    <Dialog
      open={name !== null}
      onOpenChange={(open) => !open && onClose()}
      title="Add this to the item master"
      description="It is added to the catalogue and put on the invoice. Next time, it will be there to pick."
      footer={
        <>
          <Button variant="ghost" onClick={onClose}>
            Cancel
          </Button>

          <Button
            disabled={!valid}
            onClick={() => {
              if (!valid || unitPrice === null) return;

              onCreate({
                code: code.trim().toUpperCase(),
                name: itemName.trim(),
                unitPrice,
                taxRate,

                // A brand-new item has no stock until somebody receives some. Nothing here pretends
                // otherwise — Phase 3 makes the balance a sum of movements, not a number you type.
                inStock: 0,
              });
            }}
          >
            Add and put on the invoice
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        <Input
          label="Item name"
          required
          value={itemName}
          onChange={(event) => setItemName(event.target.value)}
        />

        <div className="grid gap-4 sm:grid-cols-2">
          <Input
            label="Item code"
            required
            placeholder="e.g. SW-8P-GIG"
            hint="How you will find it next time."
            value={code}
            onChange={(event) => setCode(event.target.value)}
          />

          <Input
            label="Selling price"
            required
            inputMode="decimal"
            placeholder="0.00"
            value={price}
            onChange={(event) => setPrice(event.target.value)}
          />
        </div>

        <fieldset>
          <legend className="text-sm font-medium text-text">VAT</legend>

          <div className="mt-2 flex gap-2">
            {[STANDARD_VAT, 0].map((rate) => (
              <label
                key={rate}
                className={cn(
                  "cursor-pointer rounded-lg border px-3 py-1.5 text-sm transition-colors duration-200",
                  taxRate === rate
                    ? "border-primary bg-primary-ghost text-primary"
                    : "border-subtle text-muted hover:border-strong hover:text-text",
                )}
              >
                <input
                  type="radio"
                  name="tax-rate"
                  className="sr-only"
                  checked={taxRate === rate}
                  onChange={() => setTaxRate(rate)}
                />
                {rate === 0 ? "Zero-rated / exempt" : `${rate}% standard`}
              </label>
            ))}
          </div>
        </fieldset>
      </div>
    </Dialog>
  );
}

/**
 * The service document's entry field: type it, press Enter, keep going.
 *
 * This is the document the business actually raises — every one of the 12,598 invoice lines in the
 * database is a typed line — so it is not the poor relation of the picker above. It gets the same
 * grid, the same totals, the same tax summary and the same keyboard contract. The only thing it does
 * not get is a catalogue, because there is nothing to look up: the line is whatever the job was.
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

/** Stock at a glance while picking — `getAllItemsStk` does this today and staff rely on it. */
function Stock({ item }: { item: CatalogueItem }): ReactNode {
  if (item.inStock === 0) return <span className="text-xs text-muted">service</span>;

  return (
    <span className={cn("text-xs", item.inStock < 5 ? "text-warning-text" : "text-muted")}>
      {item.inStock} in stock
    </span>
  );
}
