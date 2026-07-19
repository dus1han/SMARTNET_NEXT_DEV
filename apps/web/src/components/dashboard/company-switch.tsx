"use client";

/**
 * The company switch — a segmented control, sitting in the page header.
 *
 * A dropdown inside a full-width card was the wrong shape twice over. With three options there is
 * nothing to collapse, so a select hid two of them behind a click for no gain; and the card it sat in
 * cost a whole row of vertical space, which pushed the first chart below the fold on a laptop. A
 * segmented control shows every option at once, reads as a filter rather than a form field, and fits
 * beside the title.
 */

import { useQuery } from "@tanstack/react-query";
import { getReportCompanies } from "@/lib/reports";
import type { CompanyFilter } from "@/lib/dashboard";
import { cn } from "@/lib/cn";

export function CompanySwitch({
  value,
  onChange,
}: {
  value: CompanyFilter;
  onChange: (next: CompanyFilter) => void;
}) {
  const companies = useQuery({ queryKey: ["report-companies"], queryFn: getReportCompanies });

  // One company means nothing to switch between — the control would be a label pretending to be a choice.
  if ((companies.data?.length ?? 0) < 2) return null;

  const options: { key: string; label: string; value: CompanyFilter }[] = [
    { key: "all", label: "All", value: "all" },
    ...companies.data!.map((c) => ({ key: String(c.id), label: c.name, value: c.id as CompanyFilter })),
  ];

  return (
    <div
      role="group"
      aria-label="Company"
      className="inline-flex rounded-lg border border-subtle bg-surface-sunken p-0.5"
    >
      {options.map((o) => {
        const selected = String(value) === o.key;

        return (
          <button
            key={o.key}
            type="button"
            aria-pressed={selected}
            onClick={() => onChange(o.value)}
            className={cn(
              "rounded-md px-3 py-1.5 text-sm font-medium transition-colors",
              // The selected state is a filled surface, not just a colour change: on its own, colour
              // would be the only thing saying which is active.
              selected
                ? "bg-surface text-text shadow-sm"
                : "text-muted hover:text-text",
            )}
          >
            {o.label}
          </button>
        );
      })}
    </div>
  );
}
