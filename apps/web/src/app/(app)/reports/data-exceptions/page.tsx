"use client";

/**
 * Data Exceptions (LEGACY-DATA-POLICY §4) — every known defect in the imported legacy data, listed live so
 * it is visible and does not quietly grow: duplicate payment groups, invoices marked paid with no payment
 * behind them, invoices whose lines don't sum to the header, invoices paid more than they were worth,
 * payments naming an invoice that does not exist, and the supplier-side equivalents.
 *
 * Some defects offer a permission-gated, audited correction here (a duplicate removed, a missing payment
 * recorded, a receivable restored) — a real, reasoned change, never a silent edit. Because the correction
 * fixes the underlying data, the exception then self-clears from the list.
 *
 * The rest are resolved where the record lives, not from this screen, because the decision belongs to the
 * document: an overpayment is resolved by voiding the payment that should not be there (or leaving the
 * customer in credit, which is a true statement of the position), and a lines/header gap needs a per-invoice
 * decision about which of the two is right. This screen's job is to make them all findable.
 */

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { AlertTriangle, Banknote, Copy, CopyX, FileWarning, ReceiptText, Truck, Unlink, Unplug } from "lucide-react";
import { useState } from "react";
import { ApiError } from "@/lib/api";
import {
  getDataExceptions,
  dataExceptionsExportUrl,
  resolveDataException,
  type DataExceptionRow,
} from "@/lib/reports";
import { PageHeader } from "@/components/shell/app-shell";
import { DataTable, type ColumnDef } from "@/components/data-table";
import { ReportFilterBar, StatTile, formatMoney , useCompanyFilter } from "@/components/reports";
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
  const { company, setCompany } = useCompanyFilter();
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
      // Not always an invoice now: a payment naming nothing is referenced by its own id, and the supplier
      // rules reference a supplier invoice.
      header: "Reference",
      accessorFn: (row) => row.reference,
      cell: ({ row }) => <span className="tabular text-text">{row.original.reference}</span>,
    },
    {
      id: "customer",
      header: "Customer / supplier",
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

      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
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
        <StatTile
          label="Overpaid"
          icon={Banknote}
          color={data && data.overpaid > 0 ? "amber" : "emerald"}
          delayMs={210}
          value={data ? <AnimatedNumber value={data.overpaid} format={formatCount} /> : "—"}
        />
        <StatTile
          label="Payments without an invoice"
          icon={Unlink}
          color={data && data.orphanedPayments > 0 ? "amber" : "emerald"}
          delayMs={280}
          value={data ? <AnimatedNumber value={data.orphanedPayments} format={formatCount} /> : "—"}
        />
        <StatTile
          label="Supplier settlements"
          icon={Truck}
          color={data && data.supplierSettlements > 0 ? "amber" : "emerald"}
          delayMs={350}
          value={data ? <AnimatedNumber value={data.supplierSettlements} format={formatCount} /> : "—"}
        />
        <StatTile
          label="Lines without a document"
          icon={Unplug}
          color={data && data.orphanedLines > 0 ? "amber" : "emerald"}
          delayMs={420}
          value={data ? <AnimatedNumber value={data.orphanedLines} format={formatCount} /> : "—"}
        />
        <StatTile
          label="Duplicate numbers"
          icon={Copy}
          color={data && data.duplicateNumbers > 0 ? "amber" : "emerald"}
          delayMs={490}
          value={data ? <AnimatedNumber value={data.duplicateNumbers} format={formatCount} /> : "—"}
        />
      </div>

      <p className="flex items-start gap-2 text-sm text-muted">
        <AlertTriangle className="mt-0.5 size-4 shrink-0" aria-hidden />
        Correcting an exception writes a real, audited change with your reason — never a silent edit — and the
        exception clears once the data is consistent again. Rows with no Resolve button are corrected on the
        record itself: an overpaid invoice by voiding the payment that should not be there, a supplier
        settlement from the supplier payment, and a lines/header gap by editing the invoice.
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

/** Rose for money that is missing or unattributed, amber for money counted twice, indigo for the rest. */
const badgeColor = (type: string): "amber" | "rose" | "indigo" => {
  switch (type) {
    case "Duplicate payment":
    case "Overpaid":
    case "Supplier settled twice":
      return "amber";
    case "Paid, no payment":
    case "Payment without an invoice":
    case "Supplier paid, not settled":
      return "rose";
    default:
      return "indigo";
  }
};
