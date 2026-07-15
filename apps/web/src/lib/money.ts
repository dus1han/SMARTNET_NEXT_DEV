/**
 * Money, in minor units.
 *
 * ISSUES B1 — the legacy app's single most expensive defect — is that *money is a `double`*:
 * `Convert.ToDouble(...)` throughout, and not one `decimal` in the entire controller layer. Binary
 * floating point cannot represent 0.1, so totals, VAT and balances drift. On a VAT return that is
 * not a theoretical problem.
 *
 * JavaScript has no decimal type either, and `0.1 + 0.2 === 0.30000000000000004` here just as it
 * does in C#. So money never exists in this application as a fractional `number`: it is an integer
 * count of the smallest unit (fils — 1 AED = 100), and every operation that could produce a
 * fraction rounds, once, explicitly, on the way out.
 *
 * The server re-computes every total from the posted lines and is the authority (DEVELOPMENT.md §8).
 * These functions exist so that the figure under the user's cursor matches the one they will be
 * invoiced for — not so that the browser can be trusted with the arithmetic.
 */

/** An integer number of fils. 1 AED = 100 fils. Never fractional. */
export type Minor = number;

export const MINOR_UNITS_PER_MAJOR = 100;

/**
 * Quantities are held in thousandths, for the same reason money is held in fils: 0.3 kg of cable is
 * not representable as a `number` and 300 is.
 */
export const QUANTITY_SCALE = 1000;

/**
 * Commercial rounding: half away from zero.
 *
 * `Math.round` rounds half *up* (−2.5 → −2), which quietly biases every credit note and refund in
 * the customer's favour. Rounding a negative amount must mirror rounding its positive.
 */
export function roundHalf(value: number): number {
  return value < 0 ? -Math.round(-value) : Math.round(value);
}

/** "1,250.50" → 125050. Null when it is not a number — an empty cell is not zero. */
export function parseAmount(input: string): Minor | null {
  const cleaned = input.replace(/[\s,]/g, "");

  if (cleaned === "" || !/^-?\d*\.?\d*$/.test(cleaned)) return null;

  const parsed = Number(cleaned);

  return Number.isFinite(parsed) ? roundHalf(parsed * MINOR_UNITS_PER_MAJOR) : null;
}

/** 2.5 → 2500. Null when it is not a quantity. Zero is a quantity; blank is not. */
export function parseQuantity(input: string): number | null {
  const cleaned = input.replace(/[\s,]/g, "");

  if (cleaned === "" || !/^\d*\.?\d*$/.test(cleaned)) return null;

  const parsed = Number(cleaned);

  return Number.isFinite(parsed) ? roundHalf(parsed * QUANTITY_SCALE) : null;
}

/**
 * 125050 → "1,250.50".
 *
 * Grouped for reading, and always two decimals: a money column where "1,250" sits above "1,250.50"
 * is a column nobody can scan.
 */
export function formatAmount(minor: Minor): string {
  const negative = minor < 0;
  const absolute = Math.abs(minor);

  const major = Math.trunc(absolute / MINOR_UNITS_PER_MAJOR);
  const fraction = absolute % MINOR_UNITS_PER_MAJOR;

  const grouped = major.toString().replace(/\B(?=(\d{3})+(?!\d))/g, ",");

  return `${negative ? "-" : ""}${grouped}.${fraction.toString().padStart(2, "0")}`;
}

/** 2500 → "2.5". Trailing zeros dropped: nobody writes "2.500 units". */
export function formatQuantity(scaled: number): string {
  const value = scaled / QUANTITY_SCALE;

  return Number.isInteger(value) ? value.toString() : value.toFixed(3).replace(/0+$/, "");
}

/**
 * unit price × quantity, rounded once.
 *
 * The multiplication is integer × integer (fils × thousandths), so nothing is lost before the single
 * rounding step at the end. Rounding each intermediate would compound the error across a hundred
 * lines, which is how the legacy totals drift.
 */
export function extend(unitPrice: Minor, scaledQuantity: number): Minor {
  return roundHalf((unitPrice * scaledQuantity) / QUANTITY_SCALE);
}

/** A percentage of an amount, rounded once. `percent` is 5 for 5%, not 0.05. */
export function percentOf(amount: Minor, percent: number): Minor {
  return roundHalf((amount * percent) / 100);
}

/** Money is added, never averaged: a sum of integers is exact and needs no rounding. */
export function sum(amounts: readonly Minor[]): Minor {
  return amounts.reduce((total, amount) => total + amount, 0);
}
