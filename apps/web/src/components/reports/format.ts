/**
 * Display formatting for report figures.
 *
 * Report money arrives from the API as a real `decimal` serialised to a JSON number in major units
 * (e.g. `1234.56`) — the server has already parsed it, once, defensively, out of the legacy varchar
 * columns. So it is formatted for reading here, not re-parsed. (This is NOT the fils-based arithmetic
 * in lib/money.ts, which exists for the line-item editor that computes totals in the browser.)
 */
export function formatMoney(value: number): string {
  return value.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}

/** An ISO `yyyy-MM-dd` report date → a readable date, or an em dash when it was unreadable (null). */
export function formatReportDate(iso: string | null | undefined): string {
  if (!iso) return "—";

  const date = new Date(`${iso}T00:00:00`);
  return Number.isNaN(date.getTime())
    ? iso
    : date.toLocaleDateString(undefined, { dateStyle: "medium" });
}
