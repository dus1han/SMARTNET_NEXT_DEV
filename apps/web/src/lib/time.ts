/**
 * Turning the API's timestamps into instants, in one place.
 *
 * **Every timestamp the API stores is UTC** — that is a house rule, enforced down to `DateTime.Now` being
 * a banned symbol. What is easy to miss is that they do not always arrive *saying* so: a value read back
 * out of MySQL has no time zone attached, so it serialises as `2026-07-21T06:53:00` rather than
 * `...06:53:00Z`. Handed to `new Date(...)`, that is parsed as **local** time, and the reader is shown a
 * clock that is out by their whole UTC offset — 06:53 instead of 12:23 in Colombo.
 *
 * It is a quiet failure: the time looks plausible, it is simply wrong, and it is wrong in the direction
 * that makes an overnight job look like it ran in the evening. Three screens had each grown their own
 * `endsWith("Z")` guard against it and two had not, so the same instant rendered differently depending on
 * which page you were standing on. Hence one helper, used by all of them.
 */

/** Already carries a zone: a trailing Z, or a ±HH:MM offset after the time. */
const hasTimeZone = (value: string) => /(?:Z|[+-]\d{2}:?\d{2})$/.test(value.trim());

/**
 * The instant an API timestamp names, or null if it is not a timestamp at all.
 *
 * A value with no zone is taken as UTC, because that is what the API stores. A value that already carries
 * one is left alone — appending a `Z` to `...+05:30` would produce nonsense rather than a correction.
 */
export function instantFromApi(value: string | null | undefined): Date | null {
  if (!value) {
    return null;
  }

  const parsed = new Date(hasTimeZone(value) ? value : `${value}Z`);

  return Number.isNaN(parsed.getTime()) ? null : parsed;
}

/**
 * An API timestamp as the reader's own clock shows it.
 *
 * Deliberately the browser's time zone rather than a hardcoded one: the business is in Colombo, but a
 * fixed `Asia/Colombo` would be a lie to anyone reading from anywhere else, and the rest of the app has
 * always rendered in the viewer's locale.
 */
export function formatInstant(
  value: string | null | undefined,
  options: Intl.DateTimeFormatOptions = { dateStyle: "medium", timeStyle: "short" },
): string {
  return instantFromApi(value)?.toLocaleString(undefined, options) ?? "—";
}
