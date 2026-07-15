"use client";

/**
 * Customer sales report (customersales_rpt) — the Sales report's mirror, grouped per customer and
 * ranked by profit. Same spine, a different grouping and column set.
 */

import { useQuery } from "@tanstack/react-query";
import { AlertTriangle, Coins, TrendingUp, Users } from "lucide-react";
import { useState } from "react";
import { ApiError } from "@/lib/api";
import {
  customerSalesReportExportUrl,
  getCustomerSalesReport,
  type CustomerSalesRow,
} from "@/lib/reports";
import { currentMonthStart, today } from "@/lib/period";
import { PageHeader } from "@/components/shell/app-shell";
import { DataTable, type ColumnDef } from "@/components/data-table";
import { ReportFilterBar, StatTile, formatMoney } from "@/components/reports";
import { AnimatedNumber, ErrorBanner, FadeIn } from "@/components/ui";

export default function CustomerSalesReportPage() {
  const [from, setFrom] = useState(currentMonthStart);
  const [to, setTo] = useState(today);

  const report = useQuery({
    queryKey: ["customer-sales-report", from, to],
    queryFn: () => getCustomerSalesReport({ from, to }),
  });

  const loadError = report.error as ApiError | null;
  const data = report.data;

  return (
    <FadeIn className="space-y-6">
      <PageHeader
        title="Customer sales"
        description="Sales grouped by customer for the period, ranked by profit, for the company you are working in."
      />

      <ReportFilterBar from={from} to={to} onFrom={setFrom} onTo={setTo} />

      {loadError && <ErrorBanner message={loadError.message} correlationId={loadError.correlationId} />}

      <div className="grid gap-4 sm:grid-cols-3">
        <StatTile
          label="Total sales"
          icon={TrendingUp}
          color="indigo"
          value={data ? <AnimatedNumber value={data.totalSales} format={formatMoney} /> : "—"}
        />
        <StatTile
          label="Total profit"
          icon={Coins}
          color="emerald"
          delayMs={70}
          value={data ? <AnimatedNumber value={data.totalProfit} format={formatMoney} /> : "—"}
        />
        <StatTile
          label="Customers"
          icon={Users}
          color="slate"
          delayMs={140}
          value={data ? <AnimatedNumber value={data.customerCount} format={(n) => Math.round(n).toLocaleString()} /> : "—"}
        />
      </div>

      {data && data.flaggedCount > 0 && (
        <p className="flex items-center gap-2 text-sm text-warning-text">
          <AlertTriangle className="size-4" aria-hidden />
          {data.flaggedCount} customer{data.flaggedCount === 1 ? "" : "s"} include an invoice with a
          value we could not read from the legacy data.
        </p>
      )}

      <DataTable
        columns={columns}
        rows={data?.rows}
        loading={report.isPending}
        defaultSort={{ id: "profit", desc: true }}
        searchable={(r) => `${r.customerName} ${r.customerCode}`}
        searchPlaceholder="Search customers…"
        exportUrl={customerSalesReportExportUrl({ from, to })}
        exportFilename="customer-sales.xlsx"
        empty={{ title: "No sales in this period", description: "Widen the date range." }}
      />
    </FadeIn>
  );
}

const columns: ColumnDef<CustomerSalesRow, unknown>[] = [
  {
    id: "customer",
    header: "Customer",
    accessorFn: (row) => row.customerName,
    cell: ({ row }) => {
      const r = row.original;
      return (
        <div className="flex items-center gap-2">
          <div className="min-w-0">
            <p className="truncate text-text">{r.customerName || <span className="text-muted">—</span>}</p>
            <p className="truncate text-xs text-muted">{r.customerCode}</p>
          </div>
          {r.hasDataIssue && (
            <AlertTriangle className="size-4 shrink-0 text-warning-text" aria-label="Unreadable legacy value" />
          )}
        </div>
      );
    },
  },
  {
    id: "invoices",
    header: "Invoices",
    accessorFn: (row) => row.invoiceCount,
    cell: ({ row }) => <span className="tabular text-muted">{row.original.invoiceCount}</span>,
  },
  {
    id: "total",
    header: "Total",
    accessorFn: (row) => row.total,
    cell: ({ row }) => <span className="tabular text-text">{formatMoney(row.original.total)}</span>,
  },
  {
    id: "profit",
    header: "Profit",
    accessorFn: (row) => row.profit,
    cell: ({ row }) => <span className="tabular text-text">{formatMoney(row.original.profit)}</span>,
  },
  {
    id: "balance",
    header: "Balance",
    accessorFn: (row) => row.balance,
    cell: ({ row }) => <span className="tabular text-muted">{formatMoney(row.original.balance)}</span>,
  },
];
