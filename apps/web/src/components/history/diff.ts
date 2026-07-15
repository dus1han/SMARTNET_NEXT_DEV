import { REDACTED, type FieldChange } from "@/lib/history";

/** One field, as it was and as it became. */
export interface DiffRow {
  field: string;
  before: unknown;
  after: unknown;
}

/**
 * The stored audit diff, as rows.
 *
 * The server has already done the hard half: `changes` contains only the fields that actually
 * changed, never the whole row. So this is a shape change, not a comparison.
 */
export function changeRows(changes: Record<string, FieldChange>): DiffRow[] {
  return Object.entries(changes)
    .map(([field, change]) => ({ field, before: change.from, after: change.to }))
    .sort((a, b) => a.field.localeCompare(b.field));
}

/**
 * A field-level diff between two document snapshots.
 *
 * Unlike the audit log — which stores a diff — a snapshot is the whole document, so the comparison
 * has to happen here. It is done on flattened paths (`lines[2].qty`) rather than on the object tree,
 * because "the third line's quantity went from 2 to 3" is the sentence a person wants, and a
 * tree-shaped diff makes them reconstruct it themselves.
 *
 * Unchanged fields are dropped. A document has fifty of them and two that matter.
 */
export function snapshotRows(before: string | null, after: string): DiffRow[] {
  const previous = flatten(parse(before));
  const current = flatten(parse(after));

  const fields = [...new Set([...Object.keys(previous), ...Object.keys(current)])].sort();

  return fields
    .filter((field) => !same(previous[field], current[field]))
    .map((field) => ({ field, before: previous[field], after: current[field] }));
}

/** Every field of one snapshot, flattened — what "print this version" prints. */
export function snapshotFields(json: string): { field: string; value: unknown }[] {
  return Object.entries(flatten(parse(json)))
    .map(([field, value]) => ({ field, value }))
    .sort((a, b) => a.field.localeCompare(b.field));
}

/**
 * A value as a person reads it.
 *
 * Note what is not here: locale-dependent number formatting. A diff is evidence, and evidence that
 * renders "1.234,56" on one machine and "1,234.56" on another is evidence about the reader's
 * machine. The raw stored value is what was stored.
 */
export function formatValue(value: unknown): string {
  if (value === null || value === undefined || value === "") return "—";
  if (value === REDACTED) return "hidden";
  if (typeof value === "boolean") return value ? "Yes" : "No";
  if (typeof value === "object") return JSON.stringify(value);

  return String(value);
}

/** `MustChangePassword` → "Must change password"; `lines[2].qty` is left alone. */
export function fieldLabel(field: string): string {
  if (/[.[\]]/.test(field)) return field;

  const spaced = field.replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/_/g, " ");

  return spaced.charAt(0).toUpperCase() + spaced.slice(1).toLowerCase();
}

/** Whether this row is one the log deliberately holds no value for. */
export const isRedacted = (row: DiffRow) => row.before === REDACTED || row.after === REDACTED;

function parse(json: string | null): unknown {
  if (!json) return {};

  try {
    return JSON.parse(json);
  } catch {
    // A snapshot that will not parse is a corrupt row, not a crash. It diffs as empty, and the
    // version list still renders — which is the difference between one broken version and a
    // history tab that shows nothing at all.
    return {};
  }
}

/** `{ lines: [{ qty: 2 }] }` → `{ "lines[0].qty": 2 }`. Leaves are primitives. */
function flatten(value: unknown, prefix = ""): Record<string, unknown> {
  if (value === null || typeof value !== "object") {
    return prefix ? { [prefix]: value } : {};
  }

  const entries = Array.isArray(value)
    ? value.map((item, index) => [`${prefix}[${index}]`, item] as const)
    : Object.entries(value).map(
        ([key, item]) => [prefix ? `${prefix}.${key}` : key, item] as const,
      );

  return Object.assign({}, ...entries.map(([path, item]) => flatten(item, path))) as Record<
    string,
    unknown
  >;
}

/** Both flattened to primitives by this point, so identity is enough. */
const same = (a: unknown, b: unknown) => a === b;
