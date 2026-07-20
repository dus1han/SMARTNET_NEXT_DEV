"use client";

/**
 * Profit & loss (general_ledger) — the income and expense side of the GL, read as a statement: Revenue less
 * Cost of Sales gives Gross Profit, less Expenses gives Net Profit for the period.
 */

import { useQuery } from "@tanstack/react-query";
import { Download, TrendingUp, Wallet } from "lucide-react";
import { useState } from "react";
import { ApiError } from "@/lib/api";
import {
  getProfitLossReport,
  profitLossReportExportUrl,
  type ProfitLossLine,
  type ProfitLossResponse,
} from "@/lib/reports";
import { PageHeader } from "@/components/shell/app-shell";
import { PeriodPreset, ReportFilterBar, StatTile, formatMoney , useReportFilters } from "@/components/reports";
import { downloadExcel } from "@/components/data-table/export";
import { AnimatedNumber, Button, ErrorBanner, FadeIn } from "@/components/ui";

export default function ProfitLossReportPage() {
  // Opens on the current month, with a one-click switch to all history.
  const { from, setFrom, to, setTo, company, setCompany } = useReportFilters();
  const [exporting, setExporting] = useState(false);

  const report = useQuery({
    queryKey: ["profit-loss-report", from, to, company],
    queryFn: () => getProfitLossReport({ from, to }, company),
  });

  const loadError = report.error as ApiError | null;
  const data = report.data;

  const linesOf = (section: string) => (data?.lines ?? []).filter((l) => l.section === section);

  return (
    <FadeIn className="space-y-6">
      <PageHeader
        title="Profit & loss"
        description="Revenue, cost of sales and expenses from the general ledger — the net profit for the period."
      />

      <ReportFilterBar from={from} to={to} onFrom={setFrom} onTo={setTo} company={company} onCompany={setCompany}>
        <PeriodPreset from={from} onFrom={setFrom} onTo={setTo} />
      </ReportFilterBar>

      {loadError && <ErrorBanner message={loadError.message} correlationId={loadError.correlationId} />}

      <div className="grid gap-4 sm:grid-cols-3">
        <StatTile
          label="Revenue"
          icon={TrendingUp}
          color="indigo"
          value={data ? <AnimatedNumber value={data.revenue} format={formatMoney} /> : "—"}
        />
        <StatTile
          label="Gross profit"
          icon={Wallet}
          color="violet"
          delayMs={70}
          value={data ? <AnimatedNumber value={data.grossProfit} format={formatMoney} /> : "—"}
        />
        <StatTile
          label="Net profit"
          icon={Wallet}
          color={data && data.netProfit < 0 ? "rose" : "emerald"}
          delayMs={140}
          value={data ? <AnimatedNumber value={data.netProfit} format={formatMoney} /> : "—"}
        />
      </div>

      <div className="overflow-hidden rounded-xl border border-border bg-surface">
        <div className="flex items-center justify-between border-b border-border px-5 py-3">
          <h2 className="text-sm font-medium text-text">Statement</h2>
          <Button
            variant="secondary"
            size="sm"
            pending={exporting}
            onClick={async () => {
              setExporting(true);
              try {
                await downloadExcel(profitLossReportExportUrl({ from, to }, company), "profit-loss.xlsx");
              } finally {
                setExporting(false);
              }
            }}
          >
            <Download className="size-4" aria-hidden /> Export
          </Button>
        </div>

        {report.isPending ? (
          <p className="px-5 py-8 text-center text-sm text-muted">Loading…</p>
        ) : !data || data.lines.length === 0 ? (
          <p className="px-5 py-8 text-center text-sm text-muted">No ledger activity in this period.</p>
        ) : (
          <div>
            <Section title="Revenue" lines={linesOf("Revenue")} subtotal={data.revenue} />
            <Section title="Cost of Sales" lines={linesOf("Cost of Sales")} subtotal={data.costOfSales} borderTop />
            <TotalRow label="Gross profit" value={data.grossProfit} strong borderTop />
            <Section title="Expenses" lines={linesOf("Expenses")} subtotal={data.expenses} borderTop />
            <TotalRow label="Net profit" value={data.netProfit} strong highlight borderTop />
          </div>
        )}
      </div>

      {data && data.lines.length > 0 && data.salesReconciliation && <SalesReconciliation data={data} />}
    </FadeIn>
  );
}

/**
 * Bridges the dashboard's headline "Total Sales" (gross invoiced sales, VAT included, before returns) to
 * this statement's Revenue, so the two screens visibly reconcile instead of looking like a variance. VAT is
 * collected for the tax authority, not earned; credit notes are returns that reduce the sale — subtract both
 * and gross invoiced sales equals Revenue to the cent.
 */
function SalesReconciliation({ data }: { data: ProfitLossResponse }) {
  const r = data.salesReconciliation;
  return (
    <div className="overflow-hidden rounded-xl border border-border bg-surface">
      <div className="border-b border-border px-5 py-3">
        <h2 className="text-sm font-medium text-text">Reconciliation to the dashboard</h2>
        <p className="mt-0.5 text-xs text-muted">
          Why the dashboard&rsquo;s Total Sales differs from Revenue — VAT and returns, nothing missing.
        </p>
      </div>
      <div className="py-1">
        <ReconRow label="Gross invoiced sales" hint="incl. VAT — matches the dashboard" value={r.grossInvoicedSales} />
        <ReconRow label="Less VAT collected" hint="a tax-authority liability, not revenue" value={-r.outputVat} />
        <ReconRow label="Less sales returns" hint="credit notes raised this period" value={-r.salesReturns} />
      </div>
      <div className="flex items-center justify-between border-t border-border px-5 py-3">
        <span className="font-semibold text-text">Revenue</span>
        <span className="tabular font-semibold text-text">{formatMoney(data.revenue)}</span>
      </div>
    </div>
  );
}

function ReconRow({ label, hint, value }: { label: string; hint: string; value: number }) {
  return (
    <div className="flex items-center justify-between px-5 py-1 text-sm">
      <span className="text-text">
        {label} <span className="text-muted">— {hint}</span>
      </span>
      <span className={`tabular ${value < 0 ? "text-muted" : "text-text"}`}>
        {value < 0 ? `(${formatMoney(-value)})` : formatMoney(value)}
      </span>
    </div>
  );
}

function Section({
  title,
  lines,
  subtotal,
  borderTop,
}: {
  title: string;
  lines: ProfitLossLine[];
  subtotal: number;
  borderTop?: boolean;
}) {
  return (
    <div className={borderTop ? "border-t border-border" : undefined}>
      <p className="px-5 pb-1 pt-3 text-xs font-medium uppercase tracking-wide text-muted">{title}</p>
      {lines.length === 0 ? (
        <p className="px-5 pb-1 text-sm text-muted">—</p>
      ) : (
        lines.map((l) => (
          <div key={l.code} className="flex items-center justify-between px-5 py-1 text-sm">
            <span className="text-text">
              {l.name} <span className="text-muted tabular">({l.code})</span>
            </span>
            <span className="tabular text-text">{formatMoney(l.amount)}</span>
          </div>
        ))
      )}
      {/* The subtotal rule spans the full card width, matching the section dividers — the border sits on
          this px-5 row, not on an inset inner element. */}
      <div className="mt-1 flex items-center justify-between border-t border-border px-5 py-2 text-sm">
        <span className="text-muted">Total {title.toLowerCase()}</span>
        <span className="tabular font-medium text-text">{formatMoney(subtotal)}</span>
      </div>
    </div>
  );
}

function TotalRow({
  label,
  value,
  strong,
  highlight,
  borderTop,
}: {
  label: string;
  value: number;
  strong?: boolean;
  highlight?: boolean;
  borderTop?: boolean;
}) {
  return (
    <div
      className={`flex items-center justify-between px-5 py-3 ${borderTop ? "border-t border-border" : ""} ${
        highlight ? "bg-surface-sunken" : ""
      }`}
    >
      <span className={strong ? "font-semibold text-text" : "text-text"}>{label}</span>
      <span className={`tabular ${strong ? "font-semibold" : ""} ${value < 0 ? "text-warning-text" : "text-text"}`}>
        {value < 0 ? `(${formatMoney(-value)})` : formatMoney(value)}
      </span>
    </div>
  );
}
