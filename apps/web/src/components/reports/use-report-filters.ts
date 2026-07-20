"use client";

import { useCallback, useState, useSyncExternalStore } from "react";
import { currentMonthStart, today } from "@/lib/period";
import type { CompanyFilter } from "@/lib/reports";

/**
 * REPORT FILTERS THAT SURVIVE A RELOAD.
 *
 * Every report held its window in `useState`, so the filters existed only for as long as the tab did.
 * Refreshing threw you back to the current month; a report you had narrowed to last quarter could not
 * be sent to anyone, because the URL said nothing about what you were looking at. Somebody reviewing
 * one period across four reports re-entered the same two dates four times, and lost them on reload.
 *
 * The window now lives in the query string — `?from=2026-04-01&to=2026-06-30&company=2` — so a report
 * is reloadable, bookmarkable and shareable, and the back button steps through it.
 *
 * <h3>Why `useSyncExternalStore` and not `useState` + an effect</h3>
 *
 * These pages are prerendered, so the server has no query string to read. Reading the URL in a
 * `useState` initialiser would make the first client render disagree with the server's HTML — a
 * hydration mismatch. Reading it in an effect fixes that but sets state during the effect, which
 * costs a second render pass on every mount and is what `react-hooks/set-state-in-effect` objects to.
 *
 * `useSyncExternalStore` is the API for exactly this shape: it takes a separate server snapshot (the
 * default window) and client snapshot (the URL), so React hydrates against the former and switches to
 * the latter without either a mismatch or a cascading render. Subscribing to `popstate` then makes the
 * back button work, which the effect version could not do.
 *
 * <h3>One deliberate choice</h3>
 *
 * <b>`replaceState`, not `pushState`.</b> Typing in a date field fires per keystroke; pushing each one
 * would bury the previous page under a dozen history entries. So back leaves the report rather than
 * stepping through every intermediate date — but a filter change made by the browser (back out of a
 * report and forward into it again) still restores correctly, because the URL is the source of truth.
 *
 * Defaults are omitted from the URL, so an untouched report has a clean address.
 */

/**
 * Anything reading the query string, so a write can tell them to look again.
 *
 * `replaceState` fires no event of its own — unlike `popstate`, which the browser raises on back and
 * forward — so without this a filter change would update the URL and nothing would re-render.
 */
const listeners = new Set<() => void>();

function subscribe(onChange: () => void) {
  listeners.add(onChange);
  window.addEventListener("popstate", onChange);

  return () => {
    listeners.delete(onChange);
    window.removeEventListener("popstate", onChange);
  };
}

function readParam(key: string): string | null {
  if (typeof window === "undefined") return null;
  return new URLSearchParams(window.location.search).get(key);
}

/** Writes one parameter, dropping it when it holds the default, then wakes every reader. */
function writeParam(key: string, value: string | null) {
  if (typeof window === "undefined") return;

  const params = new URLSearchParams(window.location.search);

  if (value === null) {
    params.delete(key);
  } else {
    params.set(key, value);
  }

  const query = params.toString();

  window.history.replaceState(
    null,
    "",
    query ? `${window.location.pathname}?${query}` : window.location.pathname,
  );

  listeners.forEach((notify) => notify());
}

/**
 * The current value of one parameter, or `fallback` when it is absent.
 *
 * `fallback` is captured once so that a report left open across midnight does not silently change
 * what "today" means underneath the reader.
 */
function useUrlParam(key: string, fallback: () => string, valid: (raw: string) => boolean): string {
  const [initial] = useState(fallback);

  return useSyncExternalStore(
    subscribe,
    () => {
      const raw = readParam(key);
      // Anything malformed is somebody editing the URL by hand; fall back rather than pass a filter
      // to the API that nobody can interpret.
      return raw !== null && valid(raw) ? raw : initial;
    },
    () => initial,
  );
}

const ISO_DATE = /^\d{4}-\d{2}-\d{2}$/;
const isDate = (raw: string) => ISO_DATE.test(raw);
const isCompanyId = (raw: string) => Number.isInteger(Number(raw)) && Number(raw) > 0;

function useUrlDate(key: string, fallback: () => string): [string, (value: string) => void] {
  const value = useUrlParam(key, fallback, isDate);
  const set = useCallback((next: string) => writeParam(key, next), [key]);

  return [value, set];
}

/** The company filter. `all` is the default and stays out of the URL. */
function useUrlCompany(): [CompanyFilter, (value: CompanyFilter) => void] {
  const raw = useUrlParam("company", () => "all", isCompanyId);
  const company: CompanyFilter = raw === "all" ? "all" : Number(raw);

  const set = useCallback(
    (next: CompanyFilter) => writeParam("company", next === "all" ? null : String(next)),
    [],
  );

  return [company, set];
}

/** The common shape: a from/to window plus a company. Used by eleven of the thirteen reports. */
export function useReportFilters() {
  const [from, setFrom] = useUrlDate("from", currentMonthStart);
  const [to, setTo] = useUrlDate("to", today);
  const [company, setCompany] = useUrlCompany();

  return { from, setFrom, to, setTo, company, setCompany };
}

/** The point-in-time shape: a single "as at" date plus a company. Outstanding uses this. */
export function useAsAtFilters() {
  const [asAt, setAsAt] = useUrlDate("asAt", today);
  const [company, setCompany] = useUrlCompany();

  return { asAt, setAsAt, company, setCompany };
}

/** Just the company — for a report with no date window at all. */
export function useCompanyFilter() {
  const [company, setCompany] = useUrlCompany();
  return { company, setCompany };
}
