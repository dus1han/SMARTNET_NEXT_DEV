import { describe, expect, it } from "vitest";
import { documentTotals, draftReducer, emptyDraft, lineTotals, toPayload, type DraftLine } from "./draft";

const line = (overrides: Partial<DraftLine> = {}): DraftLine => ({
  id: crypto.randomUUID(),
  itemCode: "SW-8P-GIG",
  description: "Switch, 8-port gigabit",
  quantity: 1_000,
  unitPrice: 21_000,
  discountPercent: 0,
  taxRate: 5,
  ...overrides,
});

describe("line totals", () => {
  it("discounts before tax, and taxes what is left", () => {
    // 4 × 210.00 = 840.00, less 10% = 756.00, plus 5% VAT = 793.80.
    const totals = lineTotals(line({ quantity: 4_000, discountPercent: 10 }));

    expect(totals.gross).toBe(84_000);
    expect(totals.discount).toBe(8_400);
    expect(totals.net).toBe(75_600);
    expect(totals.tax).toBe(3_780);
    expect(totals.total).toBe(79_380);
  });

  it("rounds each figure that gets printed", () => {
    // 3 × 33.33 = 99.99, less 7% = 6.9993 → 7.00 discount, net 92.99, VAT 4.6495 → 4.65.
    const totals = lineTotals(line({ quantity: 3_000, unitPrice: 3_333, discountPercent: 7 }));

    expect(totals.discount).toBe(700);
    expect(totals.net).toBe(9_299);
    expect(totals.tax).toBe(465);

    // The invoice prints net, VAT and total. Whatever it prints must add up, or an accountant will
    // reject the document — so each stated figure is rounded, and the total is the sum of them.
    expect(totals.net + totals.tax).toBe(totals.total);
  });
});

describe("document totals", () => {
  /**
   * DEVELOPMENT.md §9's non-negotiable case, in the browser: mixed VAT rates and a discount.
   *
   * The legacy document cannot represent this at all. `vatper` is a display string ("VAT(5%)") for
   * the whole invoice (ISSUES B5), so a zero-rated line beside a standard-rated one comes out wrong
   * and nothing complains. Here it is the ordinary case.
   */
  it("handles a mixed-rate document with a discount", () => {
    const totals = documentTotals([
      // 2 × 210.00 = 420.00, no discount, 5% → 21.00
      line({ quantity: 2_000, unitPrice: 21_000, taxRate: 5 }),

      // 1 × 950.00, 10% off → 855.00, 5% → 42.75
      line({ quantity: 1_000, unitPrice: 95_000, discountPercent: 10, taxRate: 5 }),

      // Zero-rated export handling: 350.00, no VAT at all.
      line({ quantity: 1_000, unitPrice: 35_000, taxRate: 0 }),
    ]);

    expect(totals.net).toBe(42_000 + 85_500 + 35_000);
    expect(totals.discount).toBe(9_500);
    expect(totals.tax).toBe(2_100 + 4_275);
    expect(totals.total).toBe(162_500 + 6_375);

    // Grouped by rate — the VAT summary block a mixed-rate invoice has to print, and the reason the
    // rate lives on the line rather than on the document.
    expect(totals.taxByRate).toEqual([
      { rate: 0, net: 35_000, tax: 0 },
      { rate: 5, net: 127_500, tax: 6_375 },
    ]);
  });

  it("is zero for an empty document, not NaN", () => {
    expect(documentTotals([])).toEqual({
      net: 0,
      discount: 0,
      tax: 0,
      total: 0,
      taxByRate: [],
    });
  });
});

describe("the draft", () => {
  it("adds, edits and removes lines without touching a server", () => {
    const added = draftReducer(emptyDraft, {
      type: "add",
      id: "line-1",
      line: {
        itemCode: "SW-8P-GIG",
        description: "Switch, 8-port gigabit",
        quantity: 1_000,
        unitPrice: 21_000,
        discountPercent: 0,
        taxRate: 5,
      },
    });

    expect(added.lines).toHaveLength(1);

    const edited = draftReducer(added, { type: "update", id: "line-1", patch: { quantity: 3_000 } });

    expect(edited.lines[0].quantity).toBe(3_000);

    // The previous state is not mutated: React re-renders off identity, and an in-place edit to a
    // draft line is a total that silently does not update.
    expect(added.lines[0].quantity).toBe(1_000);

    expect(draftReducer(edited, { type: "remove", id: "line-1" }).lines).toHaveLength(0);
  });

  it("posts the whole document once, with integers and a snapshotted tax rate", () => {
    const payload = toPayload({
      customer: "Gulf Trading LLC",
      reference: "PO-4471",
      lines: [line({ id: "x", quantity: 2_500, taxRate: 0 })],
    });

    expect(payload.lines).toEqual([
      {
        itemCode: "SW-8P-GIG",
        description: "Switch, 8-port gigabit",
        quantityThousandths: 2_500,
        unitPriceMinor: 21_000,
        discountPercent: 0,

        // Snapshotted onto the line, not resolved at print time (ISSUES B6) — change the VAT rate
        // next year and this document must still reprint with the rate it was issued under.
        taxRate: 0,
      },
    ]);
  });

  /**
   * The two kinds of line, on one document, in one table.
   *
   * The business raises item documents and service documents, which is why the legacy app has four
   * controllers per document type. What it does *not* do is keep the item: `invoice_l.itemcode` is
   * empty on all 12,598 lines, even though 780 of them carry an item's name in their description.
   * So the item line and the typed line are indistinguishable once saved, and "how many of I-153
   * did we sell?" cannot be answered at all.
   *
   * One nullable column is the whole fix, and this test is what stops it being dropped again.
   */
  it("keeps the item on an item line, and null on a typed one", () => {
    const payload = toPayload({
      customer: "Gulf Trading LLC",
      reference: "",
      lines: [
        line({ id: "item" }),
        line({
          id: "service",
          itemCode: null,
          description: "Supply and fix 3 network points, 2nd floor",
          unitPrice: 145_000,
        }),
      ],
    });

    expect(payload.lines.map((l) => l.itemCode)).toEqual(["SW-8P-GIG", null]);
    expect(payload.lines[1].description).toBe("Supply and fix 3 network points, 2nd floor");
  });
});
