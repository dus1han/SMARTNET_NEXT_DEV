import { extend, percentOf, sum, type Minor } from "@/lib/money";

/**
 * The draft document — in the browser, where it now lives.
 *
 * This is the migration's single biggest behavioural change (ISSUES D4). Today the line items are
 * built up in **server session state**: every line the user types is an HTTP round trip to
 * `addtoCart`, every removal is `removeQItem`, and the grid is redrawn from `cartLoad`. That cannot
 * survive a stateless API — and it is already broken today, because a session cart means two browser
 * tabs share one cart and quietly poison each other's invoice.
 *
 * Here the draft is client state, and it is posted whole, once. Nothing is sent while typing.
 */
export interface DraftDocument {
  customer: string;
  reference: string;
  lines: DraftLine[];
}

export interface DraftLine {
  /** Client-side only. The server assigns real ids at save. */
  id: string;

  /**
   * The catalogue item this line is, or **null** when it is not one.
   *
   * Both kinds are real and both must survive the save. The business raises *item* documents, picked
   * from the master, and *service* documents, whose lines are typed. The legacy app models that as
   * four controllers per document type (ISSUES D3) — and then, having gone to the trouble, **throws
   * the item away**: `invoice_l.itemcode` is empty on all 12,598 lines, while 780 of them carry a
   * description that exactly matches an item's name. The picker copies the name and discards which
   * item it was.
   *
   * So "how many of I-153 did we sell?" has no answer today. One nullable column is the whole fix.
   */
  itemCode: string | null;

  /** Always editable, on both kinds of line: the legacy item flow copies the name in to type over. */
  description: string;

  /** Thousandths — see money.ts. 2.5 units is 2500. */
  quantity: number;

  unitPrice: Minor;

  /** 0–100. Per line, because in practice one line gets the deal and the rest do not. */
  discountPercent: number;

  /**
   * Per line, and this is the point.
   *
   * The legacy schema cannot express a mixed-rate document at all: `vatper` is a *display string*
   * (`"VAT(5%)"`) applied to the whole invoice (ISSUES B5). A zero-rated line beside a standard-rated
   * one is simply not representable — the invoice comes out wrong, and nothing complains.
   */
  taxRate: number;
}

export interface LineTotals {
  gross: Minor;
  discount: Minor;
  net: Minor;
  tax: Minor;
  total: Minor;
}

export interface DocumentTotals {
  net: Minor;
  discount: Minor;
  tax: Minor;
  total: Minor;

  /**
   * VAT grouped by rate — the summary a mixed-rate document needs and the legacy one cannot print.
   * Ordered by rate so the block does not reshuffle as lines are typed.
   */
  taxByRate: { rate: number; net: Minor; tax: Minor }[];
}

/**
 * One line's arithmetic.
 *
 * Rounded at each *stated* figure — the discount, the net, the tax — because each of those is a
 * number that appears on the printed invoice, and a figure that is printed must be the figure that
 * was added up. Rounding only at the end produces a document whose lines do not sum to its total,
 * which is the version accountants reject.
 */
export function lineTotals(line: DraftLine): LineTotals {
  const gross = extend(line.unitPrice, line.quantity);
  const discount = percentOf(gross, line.discountPercent);
  const net = gross - discount;
  const tax = percentOf(net, line.taxRate);

  return { gross, discount, net, tax, total: net + tax };
}

export function documentTotals(lines: readonly DraftLine[]): DocumentTotals {
  const totals = lines.map(lineTotals);

  const byRate = new Map<number, { rate: number; net: Minor; tax: Minor }>();

  lines.forEach((line, index) => {
    const group = byRate.get(line.taxRate) ?? { rate: line.taxRate, net: 0, tax: 0 };

    group.net += totals[index].net;
    group.tax += totals[index].tax;

    byRate.set(line.taxRate, group);
  });

  return {
    net: sum(totals.map((t) => t.net)),
    discount: sum(totals.map((t) => t.discount)),
    tax: sum(totals.map((t) => t.tax)),
    total: sum(totals.map((t) => t.total)),
    taxByRate: [...byRate.values()].sort((a, b) => a.rate - b.rate),
  };
}

// --- The draft, as state -----------------------------------------------------------------------

export type DraftAction =
  | { type: "add"; line: Omit<DraftLine, "id">; id: string }
  | { type: "update"; id: string; patch: Partial<Omit<DraftLine, "id">> }
  | { type: "remove"; id: string }
  | { type: "header"; patch: Partial<Pick<DraftDocument, "customer" | "reference">> }
  | { type: "clear" }
  | { type: "restore"; draft: DraftDocument };

export const emptyDraft: DraftDocument = { customer: "", reference: "", lines: [] };

export function draftReducer(draft: DraftDocument, action: DraftAction): DraftDocument {
  switch (action.type) {
    case "add":
      return { ...draft, lines: [...draft.lines, { ...action.line, id: action.id }] };

    case "update":
      return {
        ...draft,
        lines: draft.lines.map((line) =>
          line.id === action.id ? { ...line, ...action.patch } : line,
        ),
      };

    case "remove":
      return { ...draft, lines: draft.lines.filter((line) => line.id !== action.id) };

    case "header":
      return { ...draft, ...action.patch };

    case "clear":
      return emptyDraft;

    case "restore":
      return action.draft;
  }
}

/**
 * What would be posted — one request, at save, for the whole document.
 *
 * Kept as a function rather than inlined because it is the *contract question* the prototype exists
 * to settle: this shape is what Phase 5's `POST /api/invoices` has to accept, and it is easier to
 * argue about a payload than about a screen.
 */
export function toPayload(draft: DraftDocument) {
  return {
    customer: draft.customer,
    reference: draft.reference,
    lines: draft.lines.map((line) => ({
      // Null on a service line, and *kept* on an item line — which is the entire point. The legacy
      // save drops it, and with it the answer to "how many of this did we sell, at what margin?".
      itemCode: line.itemCode,
      description: line.description,

      // Sent as the integers they are held as. A decimal serialised through a JSON `number` is a
      // double again by the time it reaches C#, and B1 is exactly that mistake.
      quantityThousandths: line.quantity,
      unitPriceMinor: line.unitPrice,
      discountPercent: line.discountPercent,

      // Snapshotted at save, not resolved at render (ISSUES B6): change the VAT rate next year and
      // this invoice must still reprint with the rate it was issued under.
      taxRate: line.taxRate,
    })),
  };
}
