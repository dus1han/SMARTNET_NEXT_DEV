"use client";

/**
 * The supplier field, searchable — the supply-side twin of the document editor's `CustomerCombobox`
 * (Phase 6). Kept standalone rather than folded into the shared line editor, so the heavily-used
 * customer-side choreography stays untouched; the supplier-invoice screen (slice 2) reuses it too.
 */

import { useId, useMemo, useState, type KeyboardEvent } from "react";
import { Search } from "lucide-react";
import type { SupplierSummary } from "@/lib/suppliers";
import { cn } from "@/lib/cn";

export function SupplierCombobox({ suppliers, value, onChange }: {
  suppliers: readonly SupplierSummary[];
  value: string;
  onChange: (id: string) => void;
}) {
  const listboxId = useId();
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState("");
  const [highlighted, setHighlighted] = useState(0);

  const selected = suppliers.find((s) => String(s.id) === value) ?? null;
  const results = useMemo(() => searchSuppliers(query, suppliers), [query, suppliers]);
  const active = results[Math.min(highlighted, results.length - 1)];

  const choose = (supplier: SupplierSummary) => {
    onChange(String(supplier.id));
    setQuery("");
    setOpen(false);
    setHighlighted(0);
  };

  const onKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    switch (event.key) {
      case "ArrowDown":
        event.preventDefault();
        setOpen(true);
        setHighlighted((current) => Math.min(current + 1, results.length - 1));
        break;
      case "ArrowUp":
        event.preventDefault();
        setHighlighted((current) => Math.max(current - 1, 0));
        break;
      case "Enter":
        event.preventDefault();
        if (open && active) choose(active);
        break;
      case "Escape":
        setOpen(false);
        setQuery("");
        break;
    }
  };

  // Closed, the field reads as the chosen supplier; open, it is a search box the user is typing into.
  const shownValue = open ? query : selected ? `${selected.code} — ${selected.name}` : "";

  return (
    <div className="space-y-1.5">
      <label className="block text-sm font-medium text-text">Supplier</label>

      <div className="relative">
        <div className="flex items-center gap-2 rounded-md border border-subtle bg-surface px-3 focus-within:border-strong focus-within:ring-2 focus-within:ring-ring/25">
          <Search className="size-4 shrink-0 text-muted" aria-hidden />
          <input
            role="combobox"
            aria-expanded={open}
            aria-controls={listboxId}
            aria-autocomplete="list"
            value={shownValue}
            placeholder="Search suppliers…"
            onFocus={() => setOpen(true)}
            onChange={(event) => {
              setQuery(event.target.value);
              setOpen(true);
              setHighlighted(0);
            }}
            onKeyDown={onKeyDown}
            // A click outside closes it; the short delay lets an option's mousedown land first.
            onBlur={() => setTimeout(() => setOpen(false), 120)}
            className="w-full bg-transparent py-2 text-sm text-text placeholder:text-muted focus:outline-none"
          />
        </div>

        {open && results.length > 0 && (
          <ul
            role="listbox"
            id={listboxId}
            className="absolute top-full z-10 mt-1 max-h-64 w-full overflow-auto rounded-lg border border-subtle bg-surface shadow-lg"
          >
            {results.map((supplier, index) => (
              <li key={supplier.id}>
                <button
                  type="button"
                  role="option"
                  aria-selected={supplier === active}
                  onMouseDown={(event) => {
                    event.preventDefault();
                    choose(supplier);
                  }}
                  onMouseEnter={() => setHighlighted(index)}
                  className={cn(
                    "flex w-full items-center gap-3 px-3 py-2 text-left text-sm",
                    supplier === active ? "bg-primary-ghost text-primary" : "text-text",
                  )}
                >
                  <span className="w-20 shrink-0 font-mono text-xs text-muted">{supplier.code}</span>
                  <span className="min-w-0 flex-1 truncate">{supplier.name}</span>
                </button>
              </li>
            ))}
          </ul>
        )}

        {open && query.trim() !== "" && results.length === 0 && (
          <div className="absolute top-full z-10 mt-1 w-full rounded-lg border border-subtle bg-surface px-3 py-2 text-sm text-muted shadow-lg">
            No supplier matches “{query.trim()}”.
          </div>
        )}
      </div>
    </div>
  );
}

/** Code and name, on any word typed — capped, so a broad query does not render the whole book. */
function searchSuppliers(query: string, suppliers: readonly SupplierSummary[], limit = 50): SupplierSummary[] {
  const terms = query.toLowerCase().split(/\s+/).filter(Boolean);
  const matches = suppliers.filter((supplier) => {
    if (terms.length === 0) return true;
    const haystack = `${supplier.code ?? ""} ${supplier.name ?? ""}`.toLowerCase();
    return terms.every((term) => haystack.includes(term));
  });
  return matches.slice(0, limit);
}
