"use client";

/**
 * Data Exceptions (LEGACY-DATA-POLICY §4) — every known defect in the imported legacy data, listed live so
 * it is visible and does not quietly grow: duplicate payment groups, invoices marked paid with no payment
 * behind them, and invoices whose lines don't sum to the header.
 *
 * Each defect offers a permission-gated, audited correction (a duplicate removed, a missing payment recorded,
 * a receivable restored) — a real, reasoned change, never a silent edit. Because the correction fixes the
 * underlying data, the exception then self-clears from the list. "Lines ≠ header" is listed but not yet
 * correctable here: it needs a per-invoice decision (which of the header or the lines is right), a later slice.
 */

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { AlertTriangle, CopyX, FileWarning, ReceiptText } from "lucide-react";
import { useState } from "react";
import { ApiError } from "@/lib/api";
import {
  getDataExceptions,
  dataExceptionsExportUrl,
  resolveDataException,
  type CompanyFilter,
  type DataExceptionRow,
} from "@/lib/reports";
import { PageHeader } from "@/components/shell/app-shell";
import { DataTable, type ColumnDef } from "@/components/data-table";
import { ReportFilterBar, StatTile, formatMoney } from "@/components/reports";
import { AnimatedNumber, Badge, Button, Dialog, ErrorBanner, FadeIn, Select, Textarea, toast } from "@/components/ui";

/** The audited corrections available for each exception type. "Lines ≠ header" has none yet. */
const RESOLUTIONS: Record<string, { value: string; label: string }[]> = {
  "Duplicate payment": [{ value: "RemoveDuplicatePayments", label: "Remove the duplicate payment(s)" }],
  "Paid, no payment": [
    { value: "RecordPayment", label: "Money was received — record the missing payment" },
    { value: "RestoreReceivable", label: "Balance was zeroed in error — restore the receivable" },
  ],
};

export default function DataExceptionsPage() {
  const [company, setCompany] = useState<CompanyFilter>("all");
  const [resolving, setResolving] = useState<DataExceptionRow | null>(null);
  const [resolution, setResolution] = useState("");
  const [reason, setReason] = useState("");

  const queryClient = useQueryClient();
  const report = useQuery({
    queryKey: ["data-exceptions", company],
    queryFn: () => getDataExceptions(company),
  });

  const resolve = useMutation({
    mutationFn: () => resolveDataException({ resolution, reference: resolving!.reference, reason: reason.trim() }),
    onSuccess: () => {
      toast.success(`${resolving!.reference} corrected.`);
      closeDialog();
      void queryClient.invalidateQueries({ queryKey: ["data-exceptions"] });
    },
    onError: (error: unknown) => toast.error(error instanceof ApiError ? error.message : "The correction failed."),
  });

  const openDialog = (row: DataExceptionRow) => {
    setResolving(row);
    setResolution(RESOLUTIONS[row.type]?.[0]?.value ?? "");
    setReason("");
  };
  const closeDialog = () => setResolving(null);

  const loadError = report.error as ApiError | null;
  const data = report.data;
  const options = resolving ? (RESOLUTIONS[resolving.type] ?? []) : [];

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
    {
      id: "actions",
      header: "",
      enableSorting: false,
      meta: { align: "right" },
      cell: ({ row }) =>
        RESOLUTIONS[row.original.type] ? (
          <Button variant="secondary" size="sm" onClick={() => openDialog(row.original)}>
            Resolve
          </Button>
        ) : null,
    },
  ];

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
        Correcting an exception writes a real, audited change with your reason — never a silent edit — and the
        exception clears once the data is consistent again.
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

      <Dialog
        open={resolving !== null}
        onOpenChange={(next) => !next && closeDialog()}
        title={`Correct ${resolving?.reference ?? ""}`}
        description={resolving?.detail}
        footer={
          <>
            <Button variant="ghost" onClick={closeDialog}>
              Cancel
            </Button>
            <Button pending={resolve.isPending} disabled={!resolution || reason.trim().length === 0} onClick={() => resolve.mutate()}>
              Apply correction
            </Button>
          </>
        }
      >
        <div className="space-y-4">
          {options.length > 1 && (
            <Select label="Correction" value={resolution} onChange={(e) => setResolution(e.target.value)}>
              {options.map((o) => (
                <option key={o.value} value={o.value}>
                  {o.label}
                </option>
              ))}
            </Select>
          )}
          {options.length === 1 && <p className="text-sm text-text">{options[0].label}.</p>}

          <Textarea
            label="Reason"
            value={reason}
            onChange={(e) => setReason(e.target.value)}
            placeholder="Why this correction is being made — recorded in the audit log."
            rows={3}
          />

          <p className="flex items-start gap-2 rounded-lg border border-subtle bg-surface-sunken p-3 text-sm text-warning-text">
            <AlertTriangle className="mt-0.5 size-4 shrink-0" aria-hidden />
            <span>This writes to the ledger and the legacy data. It cannot be undone from here.</span>
          </p>
        </div>
      </Dialog>
    </FadeIn>
  );
}

const formatCount = (n: number) => String(Math.round(n));

const badgeColor = (type: string): "amber" | "rose" | "indigo" =>
  type === "Duplicate payment" ? "amber" : type === "Paid, no payment" ? "rose" : "indigo";
