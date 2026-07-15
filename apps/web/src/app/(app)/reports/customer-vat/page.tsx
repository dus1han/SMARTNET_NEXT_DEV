"use client";

/**
 * Customer VAT report (cusvat_rpt) — output VAT on tax invoices. The legacy export filtered by a
 * corrupt company value (a session slot held the end-date); here the company comes from the token and
 * the dates from the request, so that bug cannot occur.
 */

import { useQuery } from "@tanstack/react-query";
import { AlertTriangle, Percent, Receipt } from "lucide-react";
import { useState } from "react";
import { ApiError } from "@/lib/api";
import { customerVatReportExportUrl, getCustomerVatReport, type CustomerVatRow } from "@/lib/reports";
import { currentMonthStart, today } from "@/lib/period";
import { PageHeader } from "@/components/shell/app-shell";
import { DataTable, type ColumnDef } from "@/components/data-table";
import { ReportFilterBar, StatTile, formatMoney, formatReportDate } from "@/components/reports";
import { AnimatedNumber, ErrorBanner, FadeIn } from "@/components/ui";

export default function CustomerVatReportPage() {
  const [from, setFrom] = useState(currentMonthStart);
  const [to, setTo] = useState(today);

  const report = useQuery({
    queryKey: ["customer-vat-report", from, to],
    queryFn: () => getCustomerVatReport({ from, to }),
  });

  const loadError = report.error as ApiError | null;
  const data = report.data;

  return (
    <FadeIn className="space-y-6">
      <PageHeader
        title="Customer VAT"
        description="Output VAT on tax invoices for the period, for the company you are working in."
      />

      <ReportFilterBar from={from} to={to} onFrom={setFrom} onTo={setTo} />

      {loadError && <ErrorBanner message={loadError.message} correlationId={loadError.correlationId} />}

      <div className="grid gap-4 sm:grid-cols-2">
        <StatTile
          label="Value of supplies"
          icon={Receipt}
          color="indigo"
          value={data ? <AnimatedNumber value={data.totalValue} format={formatMoney} /> : "—"}
        />
        <StatTile
          label="Output VAT"
          icon={Percent}
          color="violet"
          delayMs={70}
          value={data ? <AnimatedNumber value={data.totalVat} format={formatMoney} /> : "—"}
        />
      </div>

      {data && data.flaggedCount > 0 && (
        <p className="flex items-center gap-2 text-sm text-warning-text">
          <AlertTriangle className="size-4" aria-hidden />
          {data.flaggedCount} invoice{data.flaggedCount === 1 ? "" : "s"} carry a value we could not read
          from the legacy data.
        </p>
      )}

      <DataTable
        columns={columns}
        rows={data?.rows}
        loading={report.isPending}
        defaultSort={{ id: "date" }}
        searchable={(r) => `${r.invoiceNo} ${r.customerName} ${r.vatNumber ?? ""}`}
        searchPlaceholder="Search invoices…"
        exportUrl={customerVatReportExportUrl({ from, to })}
        exportFilename="customer-vat.xlsx"
        empty={{ title: "No tax invoices in this period", description: "Widen the date range." }}
      />
    </FadeIn>
  );
}

const columns: ColumnDef<CustomerVatRow, unknown>[] = [
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
    id: "invoice",
    header: "Invoice",
    accessorFn: (row) => row.invoiceNo,
    cell: ({ row }) => <span className="tabular text-text">{row.original.invoiceNo}</span>,
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
          {r.vatNumber && <p className="truncate text-xs text-muted">VAT {r.vatNumber}</p>}
        </div>
      );
    },
  },
  {
    id: "value",
    header: "Value",
    accessorFn: (row) => row.value,
    cell: ({ row }) => <span className="tabular text-muted">{formatMoney(row.original.value)}</span>,
  },
  {
    id: "vat",
    header: "VAT",
    accessorFn: (row) => row.vat,
    cell: ({ row }) => <span className="tabular text-text">{formatMoney(row.original.vat)}</span>,
  },
];
