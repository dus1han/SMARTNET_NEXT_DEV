import type { PermissionCatalogueEntry } from "@smartnet/api-client";

/**
 * Human labels and grouping for the permission keys.
 *
 * The keys themselves — "search_in", "cusvat_rpt" — are the server's contract, and are kept exactly
 * (typos included: the legacy app still reads columns named `chequerpt` and `jobcards_rpt`). What an
 * administrator sees, though, has to be readable, so this is the display layer: a label for each, and
 * a section to sit it in. A key with no entry here still renders — under "Other", by its raw name —
 * so a permission added on the server can never silently vanish from the screen.
 */

interface PermissionMeta {
  label: string;
  hint?: string;
}

const LABELS: Record<string, PermissionMeta> = {
  // Overview — exactly one of these two, see GROUPS below.
  dashboard: {
    label: "Management Dashboard",
    hint: "Revenue, profit, margin, supplier spend and customer concentration.",
  },
  "dashboard.operations": {
    label: "Operations Dashboard",
    hint: "Today's work and what is owed. No profit, margin or cost figures.",
  },

  // Sales
  item_qu: { label: "Item quotations" },
  service_qu: { label: "Service quotations" },
  search_qu: { label: "Search quotations" },
  item_in: { label: "Item invoices" },
  service_in: { label: "Service invoices" },
  search_in: { label: "Search invoices" },
  deleted_in: { label: "Deleted invoices" },
  new_cn: { label: "New credit note" },
  search_cn: { label: "Search credit notes" },
  payments: { label: "Customer payments" },
  customer_outstanding: { label: "Customer outstanding" },

  // Purchasing
  purchaseorder: { label: "Purchase orders" },
  search_po: { label: "Search purchase orders" },
  supplier_in: { label: "Supplier invoices" },

  // Jobs
  jobcards: { label: "Job cards" },
  jobcards_rpt: { label: "Job cards report" },

  // Master data
  customer_m: { label: "Customers" },
  supplier_m: { label: "Suppliers" },
  item_m: { label: "Items" },
  itemstock: { label: "Item stock" },

  // Money
  expenses: { label: "Expenses" },
  expenses_rpt: { label: "Expenses report" },
  cheques: { label: "Cheques" },
  chequerpt: { label: "Cheques report" },

  // Reports
  sales_rpt: { label: "Sales report" },
  customersales_rpt: { label: "Customer sales report" },
  supplierpurchase_rpt: { label: "Supplier purchase report" },
  supplierpayments_rpt: { label: "Supplier payments report" },
  cusvat_rpt: { label: "Customer VAT report" },
  suppliervat_rpt: { label: "Supplier VAT report" },
  general_ledger: { label: "Trial balance / general ledger" },

  // Documents & notes
  docstorage: { label: "Document storage" },
  notes: { label: "Notes" },
  email: { label: "Email" },

  // Administration
  users: { label: "User management" },
  "roles.manage": { label: "Role management" },
  "settings.manage": { label: "Settings" },
  "audit.view": { label: "Audit log" },
  "system.dev_admin": {
    label: "Developer",
    hint: "Bypasses company scoping.",
  },
};

/**
 * The sections, in the order they appear. Each lists the keys that belong in it.
 *
 * `exclusive` marks a section where the keys are alternatives rather than independent grants: the
 * editor renders radios instead of checkboxes and drops the select-all control, because both-on and
 * both-off are states the server refuses. Without it the screen would offer three combinations of the
 * two dashboards where only two are legal, and the administrator would find that out from a 400.
 */
const GROUPS: { title: string; keys: string[]; exclusive?: boolean }[] = [
  { title: "Overview", keys: ["dashboard", "dashboard.operations"], exclusive: true },
  {
    title: "Sales",
    keys: [
      "item_qu", "service_qu", "search_qu", "item_in", "service_in", "search_in",
      "deleted_in", "new_cn", "search_cn", "payments", "customer_outstanding",
    ],
  },
  { title: "Purchasing", keys: ["purchaseorder", "search_po", "supplier_in"] },
  { title: "Jobs", keys: ["jobcards", "jobcards_rpt"] },
  { title: "Master data", keys: ["customer_m", "supplier_m", "item_m", "itemstock"] },
  { title: "Money", keys: ["expenses", "expenses_rpt", "cheques", "chequerpt"] },
  {
    title: "Reports",
    keys: [
      "general_ledger",
      "sales_rpt", "customersales_rpt", "supplierpurchase_rpt", "supplierpayments_rpt",
      "cusvat_rpt", "suppliervat_rpt",
    ],
  },
  { title: "Documents & notes", keys: ["docstorage", "notes", "email"] },
  {
    title: "Administration",
    keys: ["users", "roles.manage", "settings.manage", "audit.view", "system.dev_admin"],
  },
];

export interface PermissionItem {
  key: string;
  label: string;
  hint?: string;
  isLegacy: boolean;
}

export interface PermissionGroup {
  title: string;
  items: PermissionItem[];
  /** The items are alternatives — exactly one is held. Rendered as radios, not checkboxes. */
  exclusive?: boolean;
}

/**
 * Turns the server's flat catalogue into labelled, grouped sections for the editor.
 *
 * Driven by the catalogue, not by the labels above: a permission the server stopped offering
 * disappears, and one it started offering appears (under "Other" until it is given a home here).
 * The screen can therefore never grant a permission that does not exist, nor hide one that does.
 */
export function groupPermissions(catalogue: PermissionCatalogueEntry[]): PermissionGroup[] {
  const byKey = new Map(catalogue.map((entry) => [entry.key, entry]));
  const placed = new Set<string>();
  const groups: PermissionGroup[] = [];

  for (const group of GROUPS) {
    const items: PermissionItem[] = [];

    for (const key of group.keys) {
      const entry = byKey.get(key);
      if (!entry) continue;

      placed.add(key);
      items.push({ key, label: LABELS[key]?.label ?? key, hint: LABELS[key]?.hint, isLegacy: entry.isLegacy });
    }

    if (items.length > 0) groups.push({ title: group.title, items, exclusive: group.exclusive });
  }

  // Anything the server offers that no group claimed — so a new permission is visible immediately,
  // by its raw key, rather than only after someone remembers to slot it in above.
  const orphans = catalogue.filter((entry) => !placed.has(entry.key));

  if (orphans.length > 0) {
    groups.push({
      title: "Other",
      items: orphans.map((entry) => ({
        key: entry.key,
        label: LABELS[entry.key]?.label ?? entry.key,
        hint: LABELS[entry.key]?.hint,
        isLegacy: entry.isLegacy,
      })),
    });
  }

  return groups;
}
