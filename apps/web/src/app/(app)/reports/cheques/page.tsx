"use client";

/**
 * Cheques report (chequerpt) — cheques by period. On the spine; the export additionally fixes the
 * legacy one-cell overwrite (created-by / created-at / printed-at are separate) and derives the
 * amount-in-words rather than reading the null column.
 */

import { useQuery } from "@tanstack/react-query";
import { AlertTriangle, Banknote, Hash } from "lucide-react";
import { useState } from "react";
import { ApiError } from "@/lib/api";
import { chequeReportExportUrl, getChequeReport, type ChequeRow, type CompanyFilter } from "@/lib/reports";
import { currentMonthStart, today } from "@/lib/period";
import { PageHeader } from "@/components/shell/app-shell";
import { DataTable, type ColumnDef } from "@/components/data-table";
import { ReportFilterBar, StatTile, formatMoney, formatReportDate } from "@/components/reports";
import { AnimatedNumber, ErrorBanner, FadeIn } from "@/components/ui";

export default function ChequesReportPage() {
  const [from, setFrom] = useState(currentMonthStart);
  const [to, setTo] = useState(today);
  const [company, setCompany] = useState<CompanyFilter>("all");

  const report = useQuery({
    queryKey: ["cheque-report", from, to, company],
    queryFn: () => getChequeReport({ from, to }, company),
  });

  const loadError = report.error as ApiError | null;
  const data = report.data;

  return (
    <FadeIn className="space-y-6">
      <PageHeader
        title="Cheques"
        description="Cheques by date for the company you are working in. The export lists each one with its amount in words."
      />

      <ReportFilterBar from={from} to={to} onFrom={setFrom} onTo={setTo} company={company} onCompany={setCompany} />

      {loadError && <ErrorBanner message={loadError.message} correlationId={loadError.correlationId} />}

      <div className="grid gap-4 sm:grid-cols-2">
        <StatTile
          label="Total"
          icon={Banknote}
          color="indigo"
          value={data ? <AnimatedNumber value={data.total} format={formatMoney} /> : "—"}
        />
        <StatTile
          label="Cheques"
          icon={Hash}
          color="slate"
          delayMs={70}
          value={data ? <AnimatedNumber value={data.count} format={(n) => Math.round(n).toLocaleString()} /> : "—"}
        />
      </div>

      {data && data.flaggedCount > 0 && (
        <p className="flex items-center gap-2 text-sm text-warning-text">
          <AlertTriangle className="size-4" aria-hidden />
          {data.flaggedCount} cheque{data.flaggedCount === 1 ? "" : "s"} carry a value we could not read
          from the legacy data.
        </p>
      )}

      <DataTable
        columns={columns}
        rows={data?.rows}
        loading={report.isPending}
        defaultSort={{ id: "date", desc: true }}
        searchable={(r) => `${r.payTo ?? ""} ${r.bank ?? ""} ${r.chequeNo ?? ""} ${r.createdBy ?? ""}`}
        searchPlaceholder="Search cheques…"
        exportUrl={chequeReportExportUrl({ from, to }, company)}
        exportFilename="cheques.xlsx"
        empty={{ title: "No cheques in this period", description: "Widen the date range." }}
      />
    </FadeIn>
  );
}

const columns: ColumnDef<ChequeRow, unknown>[] = [
  {
    id: "date",
    header: "Date",
    accessorFn: (row) => row.chequeDate ?? "",
    cell: ({ row }) => {
      const r = row.original;
      return (
        <div className="flex items-center gap-2">
          <span className="text-text">{formatReportDate(r.chequeDate)}</span>
          {r.hasDataIssue && (
            <AlertTriangle className="size-4 shrink-0 text-warning-text" aria-label="Unreadable legacy value" />
          )}
        </div>
      );
    },
  },
  {
    id: "payTo",
    header: "Pay to",
    accessorFn: (row) => row.payTo ?? "",
    cell: ({ row }) => <span className="text-text">{row.original.payTo || <span className="text-muted">—</span>}</span>,
  },
  {
    id: "amount",
    header: "Amount",
    meta: { align: "right" },
    accessorFn: (row) => row.amount,
    cell: ({ row }) => <span className="tabular text-text">{formatMoney(row.original.amount)}</span>,
  },
  {
    id: "bank",
    header: "Bank",
    accessorFn: (row) => row.bank ?? "",
    cell: ({ row }) => <span className="text-muted">{row.original.bank || "—"}</span>,
  },
  {
    id: "chequeNo",
    header: "Cheque no",
    accessorFn: (row) => row.chequeNo ?? "",
    cell: ({ row }) => <span className="tabular text-muted">{row.original.chequeNo || "—"}</span>,
  },
  {
    id: "createdBy",
    header: "Created by",
    accessorFn: (row) => row.createdBy ?? "",
    cell: ({ row }) => <span className="text-muted">{row.original.createdBy || "—"}</span>,
  },
];
