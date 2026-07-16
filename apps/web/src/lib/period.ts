/**
 * Default report windows, as ISO `yyyy-MM-dd` strings.
 *
 * A report opens on the current month rather than all of history: an unbounded default would pull
 * every invoice the company has ever raised on first paint. The user widens or narrows from there.
 */

function iso(date: Date): string {
  const year = date.getFullYear();
  const month = String(date.getMonth() + 1).padStart(2, "0");
  const day = String(date.getDate()).padStart(2, "0");
  return `${year}-${month}-${day}`;
}

/** The first day of the current month. */
export function currentMonthStart(): string {
  const now = new Date();
  return iso(new Date(now.getFullYear(), now.getMonth(), 1));
}

/** Today. */
export function today(): string {
  return iso(new Date());
}

/**
 * Whole days a document has been outstanding — its age since `date` (an ISO `yyyy-MM-dd`). Null when the
 * date is missing or implausible (a legacy row with an unreadable date falls back to year 1).
 */
export function daysDue(date: string): number | null {
  const then = Date.parse(date);
  if (Number.isNaN(then)) return null;
  const days = Math.floor((Date.now() - then) / 86_400_000);
  if (days < 0 || days > 40_000) return null;
  return days;
}

/** "2 days due" / "1 day due", or "Due" when the age is unknown. */
export function daysDueLabel(date: string): string {
  const days = daysDue(date);
  if (days == null) return "Due";
  return `${days} ${days === 1 ? "day" : "days"} due`;
}
