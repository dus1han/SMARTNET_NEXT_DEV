"use client";

/**
 * Expenses report (expenses_rpt).
 *
 * The spine's clone test. This is the sales report with a different query and a different column set —
 * and nothing else: the same filter bar, the same DataTable, the same server-side money parsing and
 * .xlsx export path. The one addition is a report-specific field (the category filter), dropped into
 * the filter bar's slot. If this had needed a second money parser or a new export path, the spine was
 * not finished.
 */

import { useQuery } from "@tanstack/react-query";
import { AlertTriangle, Banknote, Hash } from "lucide-react";
import { useState } from "react";
import { ApiError } from "@/lib/api";
import {
  expenseReportExportUrl,
  getExpenseCategories,
  getExpenseReport,
  type ExpenseReportRow,
} from "@/lib/reports";
import { currentMonthStart, today } from "@/lib/period";
import { PageHeader } from "@/components/shell/app-shell";
import { DataTable, type ColumnDef } from "@/components/data-table";
import { ReportFilterBar, StatTile, formatMoney, formatReportDate } from "@/components/reports";
import { AnimatedNumber, ErrorBanner, FadeIn, Select } from "@/components/ui";

export default function ExpensesReportPage() {
  const [from, setFrom] = useState(currentMonthStart);
  const [to, setTo] = useState(today);
  const [category, setCategory] = useState<number | undefined>(undefined);

  const categories = useQuery({ queryKey: ["expense-categories"], queryFn: getExpenseCategories });

  const report = useQuery({
    queryKey: ["expense-report", from, to, category],
    queryFn: () => getExpenseReport({ from, to }, category),
  });

  const loadError = report.error as ApiError | null;
  const summary = report.data;

  return (
    <FadeIn className="space-y-6">
      <PageHeader
        title="Expenses report"
        description="Expenses for the period, for the company you are working in, optionally by category. Every line is in the export."
      />

      <ReportFilterBar from={from} to={to} onFrom={setFrom} onTo={setTo}>
        <Select
          label="Category"
          value={category ?? ""}
          onChange={(e) => setCategory(e.target.value === "" ? undefined : Number(e.target.value))}
          className="w-52"
        >
          <option value="">All categories</option>
          {categories.data?.map((c) => (
            <option key={c.id} value={c.id}>
              {c.name}
            </option>
          ))}
        </Select>
      </ReportFilterBar>

      {loadError && <ErrorBanner message={loadError.message} correlationId={loadError.correlationId} />}

      <div className="grid gap-4 sm:grid-cols-2">
        <StatTile
          label="Total"
          icon={Banknote}
          color="indigo"
          delayMs={0}
          value={summary ? <AnimatedNumber value={summary.total} format={formatMoney} /> : "—"}
        />
        <StatTile
          label="Entries"
          icon={Hash}
          color="slate"
          delayMs={70}
          value={
            summary ? <AnimatedNumber value={summary.count} format={(n) => Math.round(n).toLocaleString()} /> : "—"
          }
        />
      </div>

      {summary && summary.flaggedCount > 0 && (
        <p className="flex items-center gap-2 text-sm text-warning-text">
          <AlertTriangle className="size-4" aria-hidden />
          {summary.flaggedCount} of {summary.count} entries carry an amount we could not read from the
          legacy data. It is counted as zero and the row is marked.
        </p>
      )}

      <DataTable
        columns={columns}
        rows={report.data?.rows}
        loading={report.isPending}
        defaultSort={{ id: "date", desc: true }}
        searchable={(r) => `${r.category} ${r.description ?? ""} ${r.addedBy ?? ""} ${r.paymentMethod ?? ""}`}
        searchPlaceholder="Search expenses…"
        exportUrl={expenseReportExportUrl({ from, to }, category)}
        exportFilename="expenses.xlsx"
        empty={{
          title: "No expenses in this period",
          description: "Widen the date range, clear the category, or check the company you are working in.",
        }}
      />
    </FadeIn>
  );
}

const columns: ColumnDef<ExpenseReportRow, unknown>[] = [
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
            <AlertTriangle
              className="size-4 shrink-0 text-warning-text"
              aria-label="This row has an amount we could not read from the legacy data."
            />
          )}
        </div>
      );
    },
  },
  {
    id: "category",
    header: "Category",
    accessorFn: (row) => row.category,
    cell: ({ row }) => <span className="text-text">{row.original.category}</span>,
  },
  {
    id: "description",
    header: "Description",
    accessorFn: (row) => row.description ?? "",
    cell: ({ row }) => (
      <span className="text-text">
        {row.original.description || <span className="text-muted">—</span>}
      </span>
    ),
  },
  {
    id: "amount",
    header: "Amount",
    accessorFn: (row) => row.amount,
    cell: ({ row }) => <span className="tabular text-text">{formatMoney(row.original.amount)}</span>,
  },
  {
    id: "addedBy",
    header: "Added by",
    accessorFn: (row) => row.addedBy ?? "",
    cell: ({ row }) => (
      <span className="text-muted">{row.original.addedBy || "—"}</span>
    ),
  },
];
