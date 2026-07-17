"use client";

/**
 * Profit & loss (general_ledger) — the income and expense side of the GL, read as a statement: Revenue less
 * Cost of Sales gives Gross Profit, less Expenses gives Net Profit for the period.
 */

import { useQuery } from "@tanstack/react-query";
import { Download, TrendingUp, Wallet } from "lucide-react";
import { useState } from "react";
import { ApiError } from "@/lib/api";
import { getProfitLossReport, profitLossReportExportUrl, type CompanyFilter, type ProfitLossLine } from "@/lib/reports";
import { currentMonthStart, today } from "@/lib/period";
import { PageHeader } from "@/components/shell/app-shell";
import { PeriodPreset, ReportFilterBar, StatTile, formatMoney } from "@/components/reports";
import { downloadExcel } from "@/components/data-table/export";
import { AnimatedNumber, Button, ErrorBanner, FadeIn } from "@/components/ui";

export default function ProfitLossReportPage() {
  // Opens on the current month, with a one-click switch to all history.
  const [from, setFrom] = useState(currentMonthStart);
  const [to, setTo] = useState(today);
  const [company, setCompany] = useState<CompanyFilter>("all");
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

      <div className="rounded-xl border border-border bg-surface">
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
          <div className="divide-y divide-border">
            <Section title="Revenue" lines={linesOf("Revenue")} subtotal={data.revenue} />
            <Section title="Cost of Sales" lines={linesOf("Cost of Sales")} subtotal={data.costOfSales} />
            <TotalRow label="Gross profit" value={data.grossProfit} strong />
            <Section title="Expenses" lines={linesOf("Expenses")} subtotal={data.expenses} />
            <TotalRow label="Net profit" value={data.netProfit} strong highlight />
          </div>
        )}
      </div>
    </FadeIn>
  );
}

function Section({ title, lines, subtotal }: { title: string; lines: ProfitLossLine[]; subtotal: number }) {
  return (
    <div className="px-5 py-3">
      <p className="mb-1 text-xs font-medium uppercase tracking-wide text-muted">{title}</p>
      {lines.length === 0 ? (
        <p className="py-1 text-sm text-muted">—</p>
      ) : (
        lines.map((l) => (
          <div key={l.code} className="flex items-center justify-between py-1 text-sm">
            <span className="text-text">
              {l.name} <span className="text-muted tabular">({l.code})</span>
            </span>
            <span className="tabular text-text">{formatMoney(l.amount)}</span>
          </div>
        ))
      )}
      <div className="mt-1 flex items-center justify-between border-t border-border pt-1 text-sm">
        <span className="text-muted">Total {title.toLowerCase()}</span>
        <span className="tabular font-medium text-text">{formatMoney(subtotal)}</span>
      </div>
    </div>
  );
}

function TotalRow({ label, value, strong, highlight }: { label: string; value: number; strong?: boolean; highlight?: boolean }) {
  return (
    <div className={`flex items-center justify-between px-5 py-3 ${highlight ? "bg-surface-sunken" : ""}`}>
      <span className={strong ? "font-semibold text-text" : "text-text"}>{label}</span>
      <span className={`tabular ${strong ? "font-semibold" : ""} ${value < 0 ? "text-warning-text" : "text-text"}`}>
        {value < 0 ? `(${formatMoney(-value)})` : formatMoney(value)}
      </span>
    </div>
  );
}
