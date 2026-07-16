"use client";

/**
 * Sales report (sales_rpt).
 *
 * The first report on the spine: cash / credit / total with profit above a list of every invoice in
 * the window. It reads the legacy invoice_h read-only — every figure parsed once, defensively, on the
 * server (a blank or malformed legacy value is flagged, never thrown on). The export is a real .xlsx
 * whose money columns are numeric cells, so the column sums; the legacy export shipped text.
 */

import { useQuery } from "@tanstack/react-query";
import { AlertTriangle, CreditCard, TrendingUp, Wallet } from "lucide-react";
import { useState } from "react";
import { ApiError } from "@/lib/api";
import { getSalesReport, salesReportExportUrl, type CompanyFilter, type SalesReportRow } from "@/lib/reports";
import { PageHeader } from "@/components/shell/app-shell";
import { DataTable, type ColumnDef } from "@/components/data-table";
import { ReportFilterBar, StatTile, formatMoney, formatReportDate } from "@/components/reports";
import { AnimatedNumber, Badge, ErrorBanner, FadeIn } from "@/components/ui";
import { currentMonthStart, today } from "@/lib/period";

export default function SalesReportPage() {
  const [from, setFrom] = useState(currentMonthStart);
  const [to, setTo] = useState(today);
  const [company, setCompany] = useState<CompanyFilter>("all");

  const report = useQuery({
    queryKey: ["sales-report", from, to, company],
    queryFn: () => getSalesReport({ from, to }, company),
  });

  const loadError = report.error as ApiError | null;
  const summary = report.data?.summary;

  return (
    <FadeIn className="space-y-6">
      <PageHeader
        title="Sales report"
        description="Cash, credit and total sales with profit for the period, for the company you are working in. Every invoice line is in the export."
      />

      <ReportFilterBar from={from} to={to} onFrom={setFrom} onTo={setTo} company={company} onCompany={setCompany} />

      {loadError && <ErrorBanner message={loadError.message} correlationId={loadError.correlationId} />}

      <div className="grid gap-4 sm:grid-cols-3">
        <StatTile
          label="Cash"
          icon={Wallet}
          color="emerald"
          delayMs={0}
          value={summary ? <AnimatedNumber value={summary.cashSales} format={formatMoney} /> : "—"}
          sub={summary ? `Profit ${formatMoney(summary.cashProfit)}` : undefined}
        />
        <StatTile
          label="Credit"
          icon={CreditCard}
          color="indigo"
          delayMs={70}
          value={summary ? <AnimatedNumber value={summary.creditSales} format={formatMoney} /> : "—"}
          sub={summary ? `Profit ${formatMoney(summary.creditProfit)}` : undefined}
        />
        <StatTile
          label="Total"
          icon={TrendingUp}
          color="violet"
          delayMs={140}
          value={summary ? <AnimatedNumber value={summary.totalSales} format={formatMoney} /> : "—"}
          sub={summary ? `Profit ${formatMoney(summary.totalProfit)}` : undefined}
        />
      </div>

      {summary && summary.flaggedCount > 0 && (
        <p className="flex items-center gap-2 text-sm text-warning-text">
          <AlertTriangle className="size-4" aria-hidden />
          {summary.flaggedCount} of {summary.invoiceCount} invoices carry a value we could not read
          from the legacy data. It is counted as zero and the row is marked.
        </p>
      )}

      <DataTable
        columns={columns}
        rows={report.data?.rows}
        loading={report.isPending}
        defaultSort={{ id: "date", desc: true }}
        searchable={(r) => `${r.invoiceNo} ${r.customerName} ${r.customerCode} ${r.type}`}
        searchPlaceholder="Search invoices…"
        exportUrl={salesReportExportUrl({ from, to }, company)}
        exportFilename="sales.xlsx"
        empty={{
          title: "No sales in this period",
          description: "Widen the date range, or check the company you are working in.",
        }}
      />
    </FadeIn>
  );
}

const columns: ColumnDef<SalesReportRow, unknown>[] = [
  {
    id: "invoice",
    header: "Invoice",
    accessorFn: (row) => row.invoiceNo,
    cell: ({ row }) => {
      const r = row.original;
      return (
        <div className="flex items-center gap-2">
          <div className="min-w-0">
            <p className="truncate font-medium text-text">{r.invoiceNo}</p>
            <p className="truncate text-xs text-muted">{r.category}</p>
          </div>
          {r.hasDataIssue && (
            <AlertTriangle
              className="size-4 shrink-0 text-warning-text"
              aria-label="This row has a value we could not read from the legacy data."
            />
          )}
        </div>
      );
    },
  },
  {
    id: "type",
    header: "Type",
    accessorFn: (row) => row.type,
    cell: ({ row }) => {
      const type = row.original.type;
      return type ? (
        <Badge tone={type.toLowerCase() === "cash" ? "success" : "neutral"}>{type}</Badge>
      ) : (
        <span className="text-muted">—</span>
      );
    },
  },
  {
    id: "date",
    header: "Date",
    accessorFn: (row) => row.date ?? "",
    cell: ({ row }) => <span className="text-text">{formatReportDate(row.original.date)}</span>,
  },
  {
    id: "customer",
    header: "Customer",
    accessorFn: (row) => row.customerName,
    cell: ({ row }) => {
      const r = row.original;
      return (
        <div className="min-w-0">
          <p className="truncate text-text">{r.customerName || <span className="text-muted">—</span>}</p>
          <p className="truncate text-xs text-muted">{r.customerCode}</p>
        </div>
      );
    },
  },
  {
    id: "total",
    header: "Total",
    meta: { align: "right" },
    accessorFn: (row) => row.total,
    cell: ({ row }) => <span className="tabular text-text">{formatMoney(row.original.total)}</span>,
  },
  {
    id: "profit",
    header: "Profit",
    meta: { align: "right" },
    accessorFn: (row) => row.profit,
    cell: ({ row }) => <span className="tabular text-text">{formatMoney(row.original.profit)}</span>,
  },
  {
    id: "balance",
    header: "Balance",
    meta: { align: "right" },
    accessorFn: (row) => row.balance,
    cell: ({ row }) => (
      <span className="tabular text-muted">{formatMoney(row.original.balance)}</span>
    ),
  },
];
