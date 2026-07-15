"use client";

import { Building2 } from "lucide-react";
import { cn } from "@/lib/cn";
import type { CompanyFilter, DashboardCompanyOption } from "@/lib/dashboard";

/**
 * The dashboard's Company card — a KPI-sized tile that is also the company filter.
 *
 * Rather than a fourth read-only stat, this is where the user scopes the whole dashboard: "All" (every
 * company they may see, aggregated) or one company. It is styled as the same pastel tile as its
 * neighbours, so the row stays uniform; the selection is a segmented set of pills, which shows every
 * option at once (there are only a few companies) instead of hiding them behind a dropdown.
 */
export function CompanyFilterCard({ companies, selected, onChange, delayMs = 0 }: {
  companies: DashboardCompanyOption[];
  selected: CompanyFilter;
  onChange: (value: CompanyFilter) => void;
  delayMs?: number;
}) {
  const options: { value: CompanyFilter; label: string }[] = [
    { value: "all", label: "All" },
    ...companies.map((c) => ({ value: c.id as CompanyFilter, label: c.name })),
  ];

  return (
    <div
      className={cn(
        "stat-tile stat-violet group",
        "animate-in fade-in-0 slide-in-from-bottom-3 fill-mode-backwards duration-500 ease-out",
        "transition-transform duration-200 hover:-translate-y-1",
      )}
      style={{ animationDelay: `${delayMs}ms` }}
    >
      <div className="flex items-center justify-between gap-2">
        <p className="stat-label text-xs font-semibold uppercase tracking-wider">Company</p>
        <span className="stat-chip grid size-9 place-items-center rounded-lg">
          <Building2 className="size-5" aria-hidden />
        </span>
      </div>

      <div className="mt-3 flex flex-wrap gap-1.5" role="group" aria-label="Filter the dashboard by company">
        {options.map((o) => {
          const active = o.value === selected;
          return (
            <button
              key={String(o.value)}
              type="button"
              onClick={() => onChange(o.value)}
              aria-pressed={active}
              className={cn(
                "rounded-full px-3 py-1 text-xs font-medium transition-[background-color,color] duration-150",
                "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-current",
                !active && "hover:brightness-[0.97]",
              )}
              style={
                active
                  ? { background: "var(--stat-fg)", color: "#fff" }
                  : { background: "color-mix(in srgb, var(--stat-fg), transparent 86%)", color: "var(--stat-fg)" }
              }
            >
              {o.label}
            </button>
          );
        })}
      </div>
    </div>
  );
}
