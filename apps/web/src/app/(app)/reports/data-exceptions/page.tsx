"use client";

/**
 * Data Exceptions (LEGACY-DATA-POLICY §4) — every known defect in the imported legacy data, listed live so
 * it is visible and does not quietly grow: duplicate payment groups, invoices marked paid with no payment
 * behind them, and invoices whose lines don't sum to the header. Read-only for now — the permission-gated,
 * audited correction is a later slice; today the value is that nothing is hidden.
 */

import { useQuery } from "@tanstack/react-query";
import { AlertTriangle, CopyX, FileWarning, ReceiptText } from "lucide-react";
import { useState } from "react";
import { ApiError } from "@/lib/api";
import { getDataExceptions, dataExceptionsExportUrl, type CompanyFilter, type DataExceptionRow } from "@/lib/reports";
import { PageHeader } from "@/components/shell/app-shell";
import { DataTable, type ColumnDef } from "@/components/data-table";
import { ReportFilterBar, StatTile, formatMoney } from "@/components/reports";
import { AnimatedNumber, Badge, ErrorBanner, FadeIn } from "@/components/ui";

export default function DataExceptionsPage() {
  const [company, setCompany] = useState<CompanyFilter>("all");

  const report = useQuery({
    queryKey: ["data-exceptions", company],
    queryFn: () => getDataExceptions(company),
  });

  const loadError = report.error as ApiError | null;
  const data = report.data;

  return (
    <FadeIn className="space-y-6">
      <PageHeader
        title="Data exceptions"
        description="Known defects in the imported legacy data, listed live so they are visible and do not quietly grow."
      />

      <ReportFilterBar company={company} onCompany={setCompany} showDates={false} />

      {loadError && <ErrorBanner message={loadError.message} correlationId={loadError.correlationId} />}

      <div className="grid gap-4 sm:grid-cols-3">
        <StatTile
          label="Duplicate payments"
          icon={CopyX}
          color={data && data.duplicatePayments > 0 ? "amber" : "emerald"}
          value={data ? <AnimatedNumber value={data.duplicatePayments} format={formatCount} /> : "—"}
        />
        <StatTile
          label="Paid, no payment"
          icon={ReceiptText}
          color={data && data.paidNoPayment > 0 ? "amber" : "emerald"}
          delayMs={70}
          value={data ? <AnimatedNumber value={data.paidNoPayment} format={formatCount} /> : "—"}
        />
        <StatTile
          label="Lines ≠ header"
          icon={FileWarning}
          color={data && data.linesNotHeader > 0 ? "amber" : "emerald"}
          delayMs={140}
          value={data ? <AnimatedNumber value={data.linesNotHeader} format={formatCount} /> : "—"}
        />
      </div>

      <p className="flex items-start gap-2 text-sm text-muted">
        <AlertTriangle className="mt-0.5 size-4 shrink-0" aria-hidden />
        These are shown as they stand in the legacy data — read-only. Corrections are made as a proper, audited
        adjustment when the business is ready, never by silently editing history.
      </p>

      <DataTable
        columns={columns}
        rows={data?.rows}
        loading={report.isPending}
        defaultSort={{ id: "amount", desc: true }}
        searchable={(r) => `${r.type} ${r.reference} ${r.customerName} ${r.detail}`}
        searchPlaceholder="Search exceptions…"
        exportUrl={dataExceptionsExportUrl(company)}
        exportFilename="data-exceptions.xlsx"
        empty={{
          title: "No data exceptions",
          description: "The imported data for this company is clean.",
        }}
      />
    </FadeIn>
  );
}

const formatCount = (n: number) => String(Math.round(n));

const badgeColor = (type: string): "amber" | "rose" | "indigo" =>
  type === "Duplicate payment" ? "amber" : type === "Paid, no payment" ? "rose" : "indigo";

const columns: ColumnDef<DataExceptionRow, unknown>[] = [
  {
    id: "type",
    header: "Type",
    accessorFn: (row) => row.type,
    cell: ({ row }) => <Badge color={badgeColor(row.original.type)}>{row.original.type}</Badge>,
  },
  {
    id: "reference",
    header: "Invoice No",
    accessorFn: (row) => row.reference,
    cell: ({ row }) => <span className="tabular text-text">{row.original.reference}</span>,
  },
  {
    id: "customer",
    header: "Customer",
    accessorFn: (row) => row.customerName,
    cell: ({ row }) => <span className="text-text">{row.original.customerName || "—"}</span>,
  },
  {
    id: "detail",
    header: "Discrepancy",
    accessorFn: (row) => row.detail,
    cell: ({ row }) => <span className="text-muted">{row.original.detail}</span>,
  },
  {
    id: "amount",
    header: "Amount",
    accessorFn: (row) => row.amount,
    meta: { align: "right" },
    cell: ({ row }) => <span className="tabular text-text">{formatMoney(row.original.amount)}</span>,
  },
];
