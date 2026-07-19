"use client";

/**
 * Job cards report (jobcards_rpt) — jobs_m by date. Profit shows only once a job is out of PENDING; a
 * pending job has no cost or sell yet, so its profit is blank, never a misleading zero.
 */

import { useQuery } from "@tanstack/react-query";
import { AlertTriangle, Coins, TrendingUp, Wrench } from "lucide-react";
import { useState } from "react";
import { ApiError } from "@/lib/api";
import { getJobCardReport, jobCardReportExportUrl, type CompanyFilter, type JobCardRow } from "@/lib/reports";
import { currentMonthStart, today } from "@/lib/period";
import { PageHeader } from "@/components/shell/app-shell";
import { DataTable, type ColumnDef } from "@/components/data-table";
import { ReportFilterBar, StatTile, formatMoney, formatReportDate } from "@/components/reports";
import { AnimatedNumber, Badge, ErrorBanner, FadeIn } from "@/components/ui";

export default function JobCardsReportPage() {
  const [from, setFrom] = useState(currentMonthStart);
  const [to, setTo] = useState(today);
  const [company, setCompany] = useState<CompanyFilter>("all");

  const report = useQuery({
    queryKey: ["job-card-report", from, to, company],
    queryFn: () => getJobCardReport({ from, to }, company),
  });

  const loadError = report.error as ApiError | null;
  const data = report.data;

  return (
    <FadeIn className="space-y-6">
      <PageHeader
        title="Job cards"
        description="Jobs by date for the company you are working in, with cost, sell and profit. Profit is shown once a job is completed."
      />

      <ReportFilterBar from={from} to={to} onFrom={setFrom} onTo={setTo} company={company} onCompany={setCompany} />

      {loadError && <ErrorBanner message={loadError.message} correlationId={loadError.correlationId} />}

      <div className="grid gap-4 sm:grid-cols-3">
        <StatTile
          label="Total sell"
          icon={TrendingUp}
          color="indigo"
          value={data ? <AnimatedNumber value={data.totalSell} format={formatMoney} /> : "—"}
        />
        <StatTile
          label="Total profit"
          icon={Coins}
          color="emerald"
          delayMs={70}
          value={data ? <AnimatedNumber value={data.totalProfit} format={formatMoney} /> : "—"}
        />
        <StatTile
          label="Jobs"
          icon={Wrench}
          color="slate"
          delayMs={140}
          value={data ? <AnimatedNumber value={data.count} format={(n) => Math.round(n).toLocaleString()} /> : "—"}
        />
      </div>

      {data && data.flaggedCount > 0 && (
        <p className="flex items-center gap-2 text-sm text-warning-text">
          <AlertTriangle className="size-4" aria-hidden />
          {data.flaggedCount} job{data.flaggedCount === 1 ? "" : "s"} carry a cost or sell we could not
          read from the legacy data.
        </p>
      )}

      <DataTable
        columns={columns}
        rows={data?.rows}
        loading={report.isPending}
        defaultSort={{ id: "date", desc: true }}
        searchable={(r) => `${r.jobNo} ${r.customerName} ${r.status}`}
        searchPlaceholder="Search jobs…"
        exportUrl={jobCardReportExportUrl({ from, to }, company)}
        exportFilename="job-cards.xlsx"
        empty={{ title: "No jobs in this period", description: "Widen the date range." }}
      />
    </FadeIn>
  );
}

const columns: ColumnDef<JobCardRow, unknown>[] = [
  {
    id: "date",
    header: "Date",
    accessorFn: (row) => row.date ?? "",
    cell: ({ row }) => {
      const r = row.original;
      return (
        <div className="flex items-center gap-2">
          <span className="text-text">{formatReportDate(r.date)}</span>
          {r.hasDataIssue && (
            <AlertTriangle className="size-4 shrink-0 text-warning-text" aria-label="Unreadable legacy value" />
          )}
        </div>
      );
    },
  },
  {
    id: "job",
    header: "Job",
    accessorFn: (row) => row.jobNo,
    cell: ({ row }) => {
      const r = row.original;
      return (
        <div className="min-w-0">
          <p className="truncate font-medium text-text">{r.jobNo}</p>
          <p className="truncate text-xs text-muted">{r.customerName || "—"}</p>
        </div>
      );
    },
  },
  {
    id: "status",
    header: "Status",
    accessorFn: (row) => row.status,
    cell: ({ row }) => {
      const s = row.original.status;
      return s ? (
        <Badge tone={s.toUpperCase() === "CLOSED" ? "success" : "warning"}>{s}</Badge>
      ) : (
        <span className="text-muted">—</span>
      );
    },
  },
  {
    id: "sell",
    header: "Sell",
    meta: { align: "right" },
    accessorFn: (row) => row.sell,
    cell: ({ row }) => <span className="tabular text-text">{formatMoney(row.original.sell)}</span>,
  },
  {
    id: "profit",
    header: "Profit",
    meta: { align: "right" },
    accessorFn: (row) => row.profit ?? -Infinity,
    cell: ({ row }) =>
      row.original.profit != null ? (
        <span className="tabular text-text">{formatMoney(row.original.profit)}</span>
      ) : (
        <span className="text-muted">—</span>
      ),
  },
];
