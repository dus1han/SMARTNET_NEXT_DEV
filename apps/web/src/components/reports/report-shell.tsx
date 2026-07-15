"use client";

import { useQuery } from "@tanstack/react-query";
import type { LucideIcon } from "lucide-react";
import type { ReactNode } from "react";
import { getReportCompanies, type CompanyFilter } from "@/lib/reports";
import { Card, Input, Select } from "@/components/ui";
import { cn } from "@/lib/cn";

/**
 * The report filter bar — every report's, so a new report is a query and a column set (Phase 2's
 * exit criterion, carried into Phase 4).
 *
 * The filters ride the request, never the session — that is the whole reason the legacy stale-filter
 * and `cvatcomp = to` corrupt-filter bugs cannot exist here. Company is a first-class filter: "All"
 * (every company the user may see, aggregated) or one entity. It sits here, in the shared bar, so
 * every report gets it uniformly. Report-specific filters drop in via `children`.
 */
export function ReportFilterBar({ from, to, onFrom, onTo, company, onCompany, children }: {
  from: string;
  to: string;
  onFrom: (value: string) => void;
  onTo: (value: string) => void;
  company: CompanyFilter;
  onCompany: (value: CompanyFilter) => void;
  children?: ReactNode;
}) {
  const companies = useQuery({ queryKey: ["report-companies"], queryFn: getReportCompanies });

  return (
    <Card className="flex flex-wrap items-end gap-4 p-4">
      {(companies.data?.length ?? 0) > 0 && (
        <Select
          label="Company"
          value={company === "all" ? "all" : String(company)}
          onChange={(e) => onCompany(e.target.value === "all" ? "all" : Number(e.target.value))}
          className="w-48"
        >
          <option value="all">All companies</option>
          {companies.data!.map((c) => (
            <option key={c.id} value={c.id}>
              {c.name}
            </option>
          ))}
        </Select>
      )}

      <Input
        label="From"
        type="date"
        value={from}
        max={to || undefined}
        onChange={(e) => onFrom(e.target.value)}
        className="w-40"
      />
      <Input
        label="To"
        type="date"
        value={to}
        min={from || undefined}
        onChange={(e) => onTo(e.target.value)}
        className="w-40"
      />
      {children}
    </Card>
  );
}

/** The headline-figure palette — soft, coordinated pastels (defined in globals.css), one per tile. */
export type StatColor = "indigo" | "violet" | "emerald" | "amber" | "sky" | "rose" | "slate";

/**
 * A headline figure — the dashboard KPIs and the report summary tiles.
 *
 * A soft pastel card, not a white box and not a vivid block: a row of these reads as one calm family,
 * the value sitting in a deeper shade of its tile's own hue. Every tile is the same height (`h-full` in
 * a stretch grid), so one with a sub-line lines up with the ones without. The value is a `ReactNode` so
 * it can be an <AnimatedNumber> that counts up on arrival. A tinted icon chip and a lift on hover
 * finish it. The colours (and their dark-mode variants) live in globals.css as the `stat-*` classes.
 */
export function StatTile({ label, value, sub, icon: Icon, color = "indigo", delayMs = 0 }: {
  label: string;
  value: ReactNode;
  sub?: ReactNode;
  icon?: LucideIcon;
  color?: StatColor;
  delayMs?: number;
}) {
  return (
    <div
      className={cn(
        "stat-tile group",
        "animate-in fade-in-0 slide-in-from-bottom-3 fill-mode-backwards duration-500 ease-out",
        "transition-transform duration-200 hover:-translate-y-1",
        `stat-${color}`,
      )}
      style={{ animationDelay: `${delayMs}ms` }}
    >
      <div className="flex items-center justify-between gap-2">
        <p className="stat-label text-xs font-semibold uppercase tracking-wider">{label}</p>
        {Icon && (
          <span className="stat-chip grid size-9 place-items-center rounded-lg transition-transform duration-200 group-hover:scale-105">
            <Icon className="size-5" aria-hidden />
          </span>
        )}
      </div>

      <div className="mt-3 text-[26px] font-bold leading-none tabular tracking-tight">{value}</div>
      {sub && <p className="stat-sub mt-1.5 text-sm">{sub}</p>}
    </div>
  );
}
