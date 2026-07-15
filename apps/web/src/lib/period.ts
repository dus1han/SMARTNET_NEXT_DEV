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
