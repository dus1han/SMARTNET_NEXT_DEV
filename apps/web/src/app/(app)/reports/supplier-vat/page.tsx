"use client";

/** Supplier VAT report (suppliervat_rpt) — input VAT on supplier tax invoices; the mirror of Customer VAT. */

import { useQuery } from "@tanstack/react-query";
import { AlertTriangle, Percent, Truck } from "lucide-react";
import { useState } from "react";
import { ApiError } from "@/lib/api";
import { getSupplierVatReport, supplierVatReportExportUrl, type CompanyFilter, type SupplierVatRow } from "@/lib/reports";
import { currentMonthStart, today } from "@/lib/period";
import { PageHeader } from "@/components/shell/app-shell";
import { DataTable, type ColumnDef } from "@/components/data-table";
import { ReportFilterBar, StatTile, formatMoney, formatReportDate } from "@/components/reports";
import { AnimatedNumber, ErrorBanner, FadeIn } from "@/components/ui";

export default function SupplierVatReportPage() {
  const [from, setFrom] = useState(currentMonthStart);
  const [to, setTo] = useState(today);
  const [company, setCompany] = useState<CompanyFilter>("all");

  const report = useQuery({
    queryKey: ["supplier-vat-report", from, to, company],
    queryFn: () => getSupplierVatReport({ from, to }, company),
  });

  const loadError = report.error as ApiError | null;
  const data = report.data;

  return (
    <FadeIn className="space-y-6">
      <PageHeader
        title="Supplier VAT"
        description="Input VAT on supplier tax invoices for the period, for the company you are working in."
      />

      <ReportFilterBar from={from} to={to} onFrom={setFrom} onTo={setTo} company={company} onCompany={setCompany} />

      {loadError && <ErrorBanner message={loadError.message} correlationId={loadError.correlationId} />}

      <div className="grid gap-4 sm:grid-cols-2">
        <StatTile
          label="Value of purchases"
          icon={Truck}
          color="indigo"
          value={data ? <AnimatedNumber value={data.totalValue} format={formatMoney} /> : "—"}
        />
        <StatTile
          label="Input VAT"
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
        searchable={(r) => `${r.invoiceNo} ${r.supplierName} ${r.vatNumber ?? ""}`}
        searchPlaceholder="Search invoices…"
        exportUrl={supplierVatReportExportUrl({ from, to }, company)}
        exportFilename="supplier-vat.xlsx"
        empty={{ title: "No tax invoices in this period", description: "Widen the date range." }}
      />
    </FadeIn>
  );
}

const columns: ColumnDef<SupplierVatRow, unknown>[] = [
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
    id: "supplier",
    header: "Supplier",
    accessorFn: (row) => row.supplierName,
    cell: ({ row }) => {
      const r = row.original;
      return (
        <div className="min-w-0">
          <p className="truncate text-text">{r.supplierName || <span className="text-muted">—</span>}</p>
          {r.vatNumber && <p className="truncate text-xs text-muted">VAT {r.vatNumber}</p>}
        </div>
      );
    },
  },
  {
    id: "value",
    header: "Value",
    accessorFn: (row) => row.value,
    meta: { align: "right" },
    cell: ({ row }) => <span className="tabular text-muted">{formatMoney(row.original.value)}</span>,
  },
  {
    id: "vat",
    header: "VAT",
    accessorFn: (row) => row.vat,
    meta: { align: "right" },
    cell: ({ row }) => <span className="tabular text-text">{formatMoney(row.original.vat)}</span>,
  },
];
