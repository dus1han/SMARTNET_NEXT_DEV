"use client";

/**
 * Trial balance (general_ledger) — every GL account's summed debits and credits for the period, from the
 * app's own double-entry ledger. A well-formed ledger balances: total debits equal total credits.
 */

import { useQuery } from "@tanstack/react-query";
import { ArrowDownLeft, ArrowUpRight, Scale } from "lucide-react";
import { ApiError } from "@/lib/api";
import { getTrialBalanceReport, trialBalanceReportExportUrl, type TrialBalanceRow } from "@/lib/reports";
import { PageHeader } from "@/components/shell/app-shell";
import { DataTable, type ColumnDef } from "@/components/data-table";
import { PeriodPreset, ReportFilterBar, StatTile, formatMoney , useReportFilters } from "@/components/reports";
import { AnimatedNumber, ErrorBanner, FadeIn } from "@/components/ui";

export default function TrialBalanceReportPage() {
  // Opens on the current month, with a one-click switch to all history.
  const { from, setFrom, to, setTo, company, setCompany } = useReportFilters();

  const report = useQuery({
    queryKey: ["trial-balance-report", from, to, company],
    queryFn: () => getTrialBalanceReport({ from, to }, company),
  });

  const loadError = report.error as ApiError | null;
  const data = report.data;

  return (
    <FadeIn className="space-y-6">
      <PageHeader
        title="Trial balance"
        description="Every general-ledger account's debits and credits for the period. A sound ledger balances — total debits equal total credits."
      />

      <ReportFilterBar from={from} to={to} onFrom={setFrom} onTo={setTo} company={company} onCompany={setCompany}>
        <PeriodPreset from={from} onFrom={setFrom} onTo={setTo} />
      </ReportFilterBar>

      {loadError && <ErrorBanner message={loadError.message} correlationId={loadError.correlationId} />}

      <div className="grid gap-4 sm:grid-cols-2">
        <StatTile
          label="Total debits"
          icon={ArrowDownLeft}
          color="indigo"
          value={data ? <AnimatedNumber value={data.totalDebit} format={formatMoney} /> : "—"}
        />
        <StatTile
          label="Total credits"
          icon={ArrowUpRight}
          color="violet"
          delayMs={70}
          value={data ? <AnimatedNumber value={data.totalCredit} format={formatMoney} /> : "—"}
        />
      </div>

      {data && (
        <p className={`flex items-center gap-2 text-sm ${data.balances ? "text-muted" : "text-warning-text"}`}>
          <Scale className="size-4 shrink-0" aria-hidden />
          {data.balances
            ? "In balance — total debits equal total credits."
            : "Out of balance — total debits do not equal total credits. This should never happen; please report it."}
        </p>
      )}

      <DataTable
        columns={columns}
        rows={data?.rows}
        loading={report.isPending}
        defaultSort={{ id: "code" }}
        searchable={(r) => `${r.code} ${r.name} ${r.type}`}
        searchPlaceholder="Search accounts…"
        exportUrl={trialBalanceReportExportUrl({ from, to }, company)}
        exportFilename="trial-balance.xlsx"
        empty={{ title: "No ledger entries in this period", description: "Widen the date range." }}
      />
    </FadeIn>
  );
}

const columns: ColumnDef<TrialBalanceRow, unknown>[] = [
  {
    id: "code",
    header: "Code",
    accessorFn: (row) => row.code,
    cell: ({ row }) => <span className="tabular text-muted">{row.original.code}</span>,
  },
  {
    id: "account",
    header: "Account",
    accessorFn: (row) => row.name,
    cell: ({ row }) => <span className="text-text">{row.original.name}</span>,
  },
  {
    id: "type",
    header: "Type",
    accessorFn: (row) => row.type,
    cell: ({ row }) => <span className="text-muted">{row.original.type}</span>,
  },
  {
    id: "debit",
    header: "Debit",
    accessorFn: (row) => row.debit,
    meta: { align: "right" },
    cell: ({ row }) => (
      <span className="tabular text-text">{row.original.debit ? formatMoney(row.original.debit) : "—"}</span>
    ),
  },
  {
    id: "credit",
    header: "Credit",
    accessorFn: (row) => row.credit,
    meta: { align: "right" },
    cell: ({ row }) => (
      <span className="tabular text-text">{row.original.credit ? formatMoney(row.original.credit) : "—"}</span>
    ),
  },
  {
    id: "balance",
    header: "Balance",
    accessorFn: (row) => row.balance,
    meta: { align: "right" },
    cell: ({ row }) => {
      const b = row.original.balance;
      // Debit balance shown plain, credit balance parenthesised — the accounting convention.
      return (
        <span className={`tabular ${b < 0 ? "text-warning-text" : "text-text"}`}>
          {b < 0 ? `(${formatMoney(-b)})` : formatMoney(b)}
        </span>
      );
    },
  },
];
