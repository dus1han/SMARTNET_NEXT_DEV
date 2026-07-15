import type { Minor } from "@/lib/money";

/**
 * PROTOTYPE DATA. Not real stock, not from the database.
 *
 * There is no items endpoint until Phase 3, and this slice must not grow one — Phase 2 writes no
 * business logic (PHASE-2-PLAN.md). What is being tested here is the *entry flow*: whether a person
 * who types invoices all day can go faster than they can today. That question does not need real
 * item data; it needs enough items that searching feels like searching.
 *
 * When Phase 3 lands, this file is deleted and the picker queries `/api/items`. The component's
 * props do not change — which is the only design decision in it that matters.
 */
export interface CatalogueItem {
  code: string;
  name: string;
  unitPrice: Minor;

  /** Per item, so a mixed-rate document happens by accident rather than by effort — see ISSUES B5. */
  taxRate: number;

  /** Shown while picking. The legacy screen's `getAllItemsStk` does the same, and staff rely on it. */
  inStock: number;
}

/** The standard UAE VAT rate. Zero-rated and exempt lines sit beside it, which is the whole point. */
const VAT = 5;

export const CATALOGUE: CatalogueItem[] = [
  { code: "CBL-CAT6-305", name: "Cat6 UTP cable, 305m box", unitPrice: 42_000, taxRate: VAT, inStock: 24 },
  { code: "CBL-CAT6A-305", name: "Cat6A S/FTP cable, 305m box", unitPrice: 78_500, taxRate: VAT, inStock: 6 },
  { code: "CBL-FIB-OM4", name: "OM4 fibre patch cord, 3m LC-LC", unitPrice: 6_500, taxRate: VAT, inStock: 140 },
  { code: "CON-RJ45-100", name: "RJ45 connectors, pack of 100", unitPrice: 4_500, taxRate: VAT, inStock: 88 },
  { code: "PAN-24P-1U", name: "Patch panel, 24-port Cat6, 1U", unitPrice: 18_000, taxRate: VAT, inStock: 31 },
  { code: "SW-8P-GIG", name: "Switch, 8-port gigabit unmanaged", unitPrice: 21_000, taxRate: VAT, inStock: 45 },
  { code: "SW-24P-POE", name: "Switch, 24-port gigabit PoE+ managed", unitPrice: 189_000, taxRate: VAT, inStock: 7 },
  { code: "SW-48P-L3", name: "Switch, 48-port L3 managed, 10G uplinks", unitPrice: 640_000, taxRate: VAT, inStock: 2 },
  { code: "RTR-ENT-01", name: "Enterprise router, dual WAN", unitPrice: 235_000, taxRate: VAT, inStock: 4 },
  { code: "AP-WIFI6", name: "Wi-Fi 6 access point, ceiling mount", unitPrice: 47_500, taxRate: VAT, inStock: 38 },
  { code: "AP-WIFI6E", name: "Wi-Fi 6E access point, tri-band", unitPrice: 82_000, taxRate: VAT, inStock: 11 },
  { code: "FW-UTM-100", name: "UTM firewall appliance, 100 users", unitPrice: 415_000, taxRate: VAT, inStock: 3 },
  { code: "RCK-42U", name: "Server rack, 42U floor standing", unitPrice: 310_000, taxRate: VAT, inStock: 2 },
  { code: "RCK-9U-WM", name: "Wall-mount cabinet, 9U", unitPrice: 68_000, taxRate: VAT, inStock: 9 },
  { code: "UPS-1KVA", name: "UPS, 1 kVA line-interactive", unitPrice: 54_000, taxRate: VAT, inStock: 17 },
  { code: "UPS-6KVA", name: "UPS, 6 kVA online, rack mount", unitPrice: 445_000, taxRate: VAT, inStock: 1 },
  { code: "CAM-DOME-4MP", name: "IP camera, 4MP dome, IR", unitPrice: 32_000, taxRate: VAT, inStock: 62 },
  { code: "NVR-16CH", name: "NVR, 16-channel with 4TB", unitPrice: 128_000, taxRate: VAT, inStock: 8 },
  { code: "SRV-1U-XEON", name: "Server, 1U Xeon, 64GB RAM", unitPrice: 1_450_000, taxRate: VAT, inStock: 1 },
  { code: "NAS-4BAY", name: "NAS, 4-bay diskless", unitPrice: 175_000, taxRate: VAT, inStock: 5 },
  { code: "SFP-10G-SR", name: "SFP+ transceiver, 10G SR", unitPrice: 12_500, taxRate: VAT, inStock: 54 },
  { code: "TOOL-CRIMP", name: "Crimping tool, ratchet RJ45", unitPrice: 9_500, taxRate: VAT, inStock: 22 },

  // The lines that break the legacy document model. A zero-rated export line and an exempt one,
  // sitting on the same invoice as everything above — which `vatper` as a single display string
  // cannot represent at all.
  { code: "SVC-INSTALL-DAY", name: "On-site installation, per engineer-day", unitPrice: 95_000, taxRate: VAT, inStock: 0 },
  { code: "SVC-EXPORT-GCC", name: "Export shipment handling (zero-rated)", unitPrice: 35_000, taxRate: 0, inStock: 0 },
  { code: "FEE-GOVT-PERMIT", name: "Government permit fee (exempt, disbursement)", unitPrice: 15_000, taxRate: 0, inStock: 0 },
];

/**
 * Matches on code and name, on any word the user has typed.
 *
 * Substring, not prefix: staff know the item as "24 port poe", never as "SW-24P-POE". A picker that
 * only matches from the start of the string is a picker they stop using.
 */
export function searchCatalogue(
  query: string,
  catalogue: readonly CatalogueItem[] = CATALOGUE,
  limit = 8,
): CatalogueItem[] {
  const terms = query.toLowerCase().split(/\s+/).filter(Boolean);

  if (terms.length === 0) return [];

  return catalogue
    .filter((item) => {
      const haystack = `${item.code} ${item.name}`.toLowerCase();

      return terms.every((term) => haystack.includes(term));
    })
    .slice(0, limit);
}
