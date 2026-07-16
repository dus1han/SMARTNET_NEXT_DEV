"use client";

/**
 * Supplier payments report (supplierpayments_rpt) — what we paid one supplier in a period, across
 * supplier_invoice + supplier_inv_pay. A supplier must be chosen; the window is the paid date.
 */

import { useQuery } from "@tanstack/react-query";
import { AlertTriangle, Banknote, Hash } from "lucide-react";
import { useState } from "react";
import { ApiError } from "@/lib/api";
import {
  getReportSuppliers,
  getSupplierPaymentReport,
  supplierPaymentReportExportUrl,
  type CompanyFilter,
  type SupplierPaymentRow,
} from "@/lib/reports";
import { currentMonthStart, today } from "@/lib/period";
import { PageHeader } from "@/components/shell/app-shell";
import { DataTable, type ColumnDef } from "@/components/data-table";
import { ReportFilterBar, StatTile, formatMoney, formatReportDate } from "@/components/reports";
import { AnimatedNumber, ErrorBanner, FadeIn, Select } from "@/components/ui";

export default function SupplierPaymentsReportPage() {
  const [from, setFrom] = useState(currentMonthStart);
  const [to, setTo] = useState(today);
  const [company, setCompany] = useState<CompanyFilter>("all");
  const [supplier, setSupplier] = useState<string>("all");

  const suppliers = useQuery({ queryKey: ["report-suppliers"], queryFn: getReportSuppliers });

  const report = useQuery({
    queryKey: ["supplier-payment-report", from, to, company, supplier],
    queryFn: () => getSupplierPaymentReport({ from, to }, company, supplier),
  });

  const loadError = report.error as ApiError | null;
  const data = report.data;

  return (
    <FadeIn className="space-y-6">
      <PageHeader
        title="Supplier payments"
        description="Payments made to a supplier in the period, for the company you are working in."
      />

      <ReportFilterBar from={from} to={to} onFrom={setFrom} onTo={setTo} company={company} onCompany={setCompany}>
        <Select
          label="Supplier"
          value={supplier}
          onChange={(e) => setSupplier(e.target.value)}
          className="w-56"
        >
          <option value="all">All suppliers</option>
          {suppliers.data?.map((s) => (
            <option key={s.code} value={s.code}>
              {s.name}
            </option>
          ))}
        </Select>
      </ReportFilterBar>

      {loadError && <ErrorBanner message={loadError.message} correlationId={loadError.correlationId} />}

      <div className="grid gap-4 sm:grid-cols-2">
        <StatTile
          label="Total paid"
          icon={Banknote}
          color="indigo"
          value={data ? <AnimatedNumber value={data.total} format={formatMoney} /> : "—"}
        />
        <StatTile
          label="Payments"
          icon={Hash}
          color="slate"
          delayMs={70}
          value={data ? <AnimatedNumber value={data.count} format={(n) => Math.round(n).toLocaleString()} /> : "—"}
        />
      </div>

      {data && data.flaggedCount > 0 && (
        <p className="flex items-center gap-2 text-sm text-warning-text">
          <AlertTriangle className="size-4" aria-hidden />
          {data.flaggedCount} payment{data.flaggedCount === 1 ? "" : "s"} carry a value we could not read
          from the legacy data.
        </p>
      )}

      <DataTable
        columns={columns}
        rows={data?.rows}
        loading={report.isPending}
        defaultSort={{ id: "paidDate", desc: true }}
        searchable={(r) => `${r.invoiceNo} ${r.payMethod ?? ""} ${r.reference ?? ""}`}
        searchPlaceholder="Search payments…"
        exportUrl={supplierPaymentReportExportUrl({ from, to }, company, supplier)}
        exportFilename="supplier-payments.xlsx"
        empty={{ title: "No payments in this period", description: "Widen the date range or clear the supplier filter." }}
      />
    </FadeIn>
  );
}

const columns: ColumnDef<SupplierPaymentRow, unknown>[] = [
  {
    id: "paidDate",
    header: "Paid",
    accessorFn: (row) => row.paidDate ?? "",
    cell: ({ row }) => {
      const r = row.original;
      return (
        <div className="flex items-center gap-2">
          <span className="text-text">{formatReportDate(r.paidDate)}</span>
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
    cell: ({ row }) => {
      const r = row.original;
      return (
        <div className="min-w-0">
          <p className="truncate tabular text-text">{r.invoiceNo}</p>
          <p className="truncate text-xs text-muted">{formatReportDate(r.invoiceDate)}</p>
        </div>
      );
    },
  },
  {
    id: "amount",
    header: "Amount",
    accessorFn: (row) => row.amount,
    meta: { align: "right" },
    cell: ({ row }) => <span className="tabular text-text">{formatMoney(row.original.amount)}</span>,
  },
  {
    id: "method",
    header: "Method",
    accessorFn: (row) => row.payMethod ?? "",
    cell: ({ row }) => <span className="text-muted">{row.original.payMethod || "—"}</span>,
  },
  {
    id: "reference",
    header: "Reference",
    accessorFn: (row) => row.reference ?? "",
    cell: ({ row }) => <span className="text-muted">{row.original.reference || "—"}</span>,
  },
];
