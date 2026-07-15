import { describe, expect, it } from "vitest";
import {
  extend,
  formatAmount,
  formatQuantity,
  parseAmount,
  parseQuantity,
  percentOf,
  roundHalf,
  sum,
} from "./money";

describe("money", () => {
  it("does not do what the legacy app does", () => {
    // The defect this module exists to prevent, stated as a test. In the legacy controllers money is
    // a double (ISSUES B1: "not one `decimal` in the entire controller layer"), and this is what
    // that costs: three ten-fils items do not add up to thirty fils.
    expect(0.1 + 0.2).not.toBe(0.3);

    // Integers add up.
    expect(sum([10, 10, 10])).toBe(30);
  });

  it("rounds half away from zero, in both directions", () => {
    expect(roundHalf(2.5)).toBe(3);
    expect(roundHalf(1.5)).toBe(2);

    // Math.round(-2.5) is -2, which rounds every refund and credit note in the customer's favour by
    // half a fil. Small, systematic, and exactly the kind of error a VAT audit finds.
    expect(roundHalf(-2.5)).toBe(-3);
    expect(roundHalf(-1.5)).toBe(-2);
  });

  it("parses what a person types, and refuses what they do not", () => {
    expect(parseAmount("1250.50")).toBe(125_050);
    expect(parseAmount("1,250.50")).toBe(125_050);
    expect(parseAmount("0.1")).toBe(10);

    // Blank is not zero. A cleared price cell means "I have not said yet", and treating it as free
    // is how a line goes out at nothing.
    expect(parseAmount("")).toBeNull();
    expect(parseAmount("abc")).toBeNull();
  });

  it("parses quantities in thousandths", () => {
    expect(parseQuantity("2.5")).toBe(2_500);
    expect(parseQuantity("0.333")).toBe(333);
    expect(parseQuantity("")).toBeNull();
    expect(parseQuantity("-1")).toBeNull();
  });

  it("formats money for a column somebody has to scan", () => {
    expect(formatAmount(125_050)).toBe("1,250.50");
    expect(formatAmount(100)).toBe("1.00");
    expect(formatAmount(5)).toBe("0.05");
    expect(formatAmount(-125_050)).toBe("-1,250.50");
    expect(formatAmount(145_000_000)).toBe("1,450,000.00");
  });

  it("formats a quantity as somebody would write it", () => {
    expect(formatQuantity(1_000)).toBe("1");
    expect(formatQuantity(2_500)).toBe("2.5");
  });

  it("extends price by quantity with a single rounding", () => {
    // 3 × 12.50
    expect(extend(1_250, 3_000)).toBe(3_750);

    // A third of a 100.00 box: 33.333… rounds once, at the end.
    expect(extend(10_000, 333)).toBe(3_330);

    // The classic: 0.1 × 3. In double arithmetic this is 0.30000000000000004.
    expect(extend(10, 3_000)).toBe(30);
  });

  it("takes a percentage without drifting", () => {
    expect(percentOf(10_000, 5)).toBe(500);
    expect(percentOf(3_333, 5)).toBe(167); // 166.65 → 167
    expect(percentOf(10_000, 0)).toBe(0);
  });
});
