"use client";

/**
 * Supplier purchase summary (supplierpurchase_rpt) — purchases per supplier with the pending balance.
 * "Pending" is a whole-invoice flag (the legacy model has no partial payment), reported as such.
 */

import { useQuery } from "@tanstack/react-query";
import { AlertTriangle, Clock, ShoppingCart, Truck } from "lucide-react";
import { ApiError } from "@/lib/api";
import {
  getSupplierPurchaseReport,
  supplierPurchaseReportExportUrl,  type SupplierPurchaseRow,
} from "@/lib/reports";
import { PageHeader } from "@/components/shell/app-shell";
import { DataTable, type ColumnDef } from "@/components/data-table";
import { ReportFilterBar, StatTile, formatMoney , useReportFilters } from "@/components/reports";
import { AnimatedNumber, ErrorBanner, FadeIn } from "@/components/ui";

export default function SupplierPurchaseReportPage() {
  const { from, setFrom, to, setTo, company, setCompany } = useReportFilters();

  const report = useQuery({
    queryKey: ["supplier-purchase-report", from, to, company],
    queryFn: () => getSupplierPurchaseReport({ from, to }, company),
  });

  const loadError = report.error as ApiError | null;
  const data = report.data;

  return (
    <FadeIn className="space-y-6">
      <PageHeader
        title="Supplier purchases"
        description="Purchases per supplier for the period, with the pending balance, for the company you are working in."
      />

      <ReportFilterBar from={from} to={to} onFrom={setFrom} onTo={setTo} company={company} onCompany={setCompany} />

      {loadError && <ErrorBanner message={loadError.message} correlationId={loadError.correlationId} />}

      <div className="grid gap-4 sm:grid-cols-3">
        <StatTile
          label="Total purchases"
          icon={ShoppingCart}
          color="indigo"
          value={data ? <AnimatedNumber value={data.totalPurchase} format={formatMoney} /> : "—"}
        />
        <StatTile
          label="Pending"
          icon={Clock}
          color="amber"
          delayMs={70}
          value={data ? <AnimatedNumber value={data.totalPending} format={formatMoney} /> : "—"}
        />
        <StatTile
          label="Suppliers"
          icon={Truck}
          color="slate"
          delayMs={140}
          value={data ? <AnimatedNumber value={data.supplierCount} format={(n) => Math.round(n).toLocaleString()} /> : "—"}
        />
      </div>

      {data && data.flaggedCount > 0 && (
        <p className="flex items-center gap-2 text-sm text-warning-text">
          <AlertTriangle className="size-4" aria-hidden />
          {data.flaggedCount} supplier{data.flaggedCount === 1 ? "" : "s"} include an invoice with a
          value we could not read from the legacy data.
        </p>
      )}

      <DataTable
        columns={columns}
        rows={data?.rows}
        loading={report.isPending}
        defaultSort={{ id: "pending", desc: true }}
        searchable={(r) => `${r.supplierName} ${r.supplierCode}`}
        searchPlaceholder="Search suppliers…"
        exportUrl={supplierPurchaseReportExportUrl({ from, to }, company)}
        exportFilename="supplier-purchase.xlsx"
        empty={{ title: "No purchases in this period", description: "Widen the date range." }}
      />
    </FadeIn>
  );
}

const columns: ColumnDef<SupplierPurchaseRow, unknown>[] = [
  {
    id: "supplier",
    header: "Supplier",
    accessorFn: (row) => row.supplierName,
    cell: ({ row }) => {
      const r = row.original;
      return (
        <div className="flex items-center gap-2">
          <div className="min-w-0">
            <p className="truncate text-text">{r.supplierName || <span className="text-muted">—</span>}</p>
            <p className="truncate text-xs text-muted">{r.supplierCode}</p>
          </div>
          {r.hasDataIssue && (
            <AlertTriangle className="size-4 shrink-0 text-warning-text" aria-label="Unreadable legacy value" />
          )}
        </div>
      );
    },
  },
  {
    id: "total",
    header: "Total purchases",
    accessorFn: (row) => row.totalPurchase,
    meta: { align: "right" },
    cell: ({ row }) => <span className="tabular text-text">{formatMoney(row.original.totalPurchase)}</span>,
  },
  {
    id: "pending",
    header: "Pending",
    accessorFn: (row) => row.pendingBalance,
    meta: { align: "right" },
    cell: ({ row }) => {
      const p = row.original.pendingBalance;
      return (
        <span className={p > 0 ? "tabular font-medium text-warning-text" : "tabular text-muted"}>
          {formatMoney(p)}
        </span>
      );
    },
  },
];
