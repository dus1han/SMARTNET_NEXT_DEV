"use client";

/**
 * One supplier invoice, in full — Phase 6 slice 2.
 *
 * The payable and every payment are payables-ledger entries, so the outstanding shown here is derived
 * (never a stored, mutated column) and partial payments accumulate. "Paid" is the fact that the outstanding
 * has reached zero. Record a payment settles against it; void reverses the payable through a compensating
 * entry — never by erasing history (the legacy delete orphaned its payments).
 */

import { useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useParams, useRouter } from "next/navigation";
import Link from "next/link";
import { ArrowLeft, Trash2, Wallet } from "lucide-react";
import { ApiError } from "@/lib/api";
import { deleteSupplierInvoice, getSupplierInvoice, recordSupplierPayment } from "@/lib/supplier-invoices";
import { me } from "@/lib/auth";
import { today } from "@/lib/period";
import { PageHeader } from "@/components/shell/app-shell";
import { DataTable, type ColumnDef } from "@/components/data-table";
import { History } from "@/components/history/history";
import { formatMoney, formatReportDate } from "@/components/reports";
import { Badge, Button, Card, Dialog, ErrorBanner, FadeIn, Input, Select, Skeleton, toast } from "@/components/ui";
import type { SupplierInvoicePaymentLine } from "@/lib/supplier-invoices";

export default function SupplierInvoiceViewPage() {
  const { id } = useParams<{ id: string }>();
  const invoiceId = Number(id);
  const router = useRouter();
  const queryClient = useQueryClient();

  const invoice = useQuery({
    queryKey: ["supplier-invoice", invoiceId],
    queryFn: () => getSupplierInvoice(invoiceId),
    enabled: Number.isFinite(invoiceId),
  });
  const user = useQuery({ queryKey: ["me"], queryFn: me });

  const [paying, setPaying] = useState(false);
  const [voiding, setVoiding] = useState(false);
  const error = invoice.error as ApiError | null;
  const data = invoice.data;
  const isLegacy = data?.origin === "legacy";
  const canModify = data != null && !isLegacy && (user.data?.permissions.includes("supplier_in") ?? false);

  function refresh() {
    void queryClient.invalidateQueries({ queryKey: ["supplier-invoice", invoiceId] });
    void queryClient.invalidateQueries({ queryKey: ["supplier-invoices"] });
  }

  return (
    <FadeIn className="space-y-6">
      <Link
        href="/supplier-invoices"
        className="inline-flex items-center gap-1.5 text-sm text-muted transition-colors hover:text-text"
      >
        <ArrowLeft className="size-4" aria-hidden />
        All supplier invoices
      </Link>

      <div className="flex flex-wrap items-start justify-between gap-4">
        <div className="flex flex-wrap items-center gap-3">
          <PageHeader
            title={data ? `Supplier invoice ${data.supplierReference ?? ""}`.trim() : "Supplier invoice"}
            description={data ? `${data.supplierName ?? "—"} · ${formatReportDate(data.date)}` : undefined}
          />
          {isLegacy && <Badge tone="neutral">Legacy</Badge>}
          {data && <Badge tone={data.status === "Paid" ? "success" : "warning"}>{data.status}</Badge>}
        </div>
        <div className="flex flex-wrap items-center gap-2">
          {canModify && data!.outstanding > 0 && (
            <Button onClick={() => setPaying(true)}>
              <Wallet />
              Record payment
            </Button>
          )}
          {canModify && (
            <Button variant="secondary" onClick={() => setVoiding(true)}>
              <Trash2 />
              Void
            </Button>
          )}
        </div>
      </div>

      {error && <ErrorBanner message={error.message} correlationId={error.correlationId} />}

      {invoice.isPending && <Skeleton className="h-40" />}

      {data && (
        <>
          <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-3">
            <Detail label="Company" value={data.companyName ?? "—"} />
            <Detail label="Supplier" value={data.supplierName ?? "—"} sub={data.supplierCode ?? undefined} />
            <Detail label="Supplier's reference" value={data.supplierReference || "—"} />
          </div>

          <div className="grid gap-4 sm:grid-cols-2">
            <div />
            <Card className="space-y-2 p-5">
              <Row label="Net" value={formatMoney(data.netTotal)} />
              <Row label={`VAT (${data.taxRatePercentage}%)`} value={formatMoney(data.taxAmount)} />
              <Row label="Amount" value={formatMoney(data.amount)} />
              <div className="border-t border-subtle pt-2">
                <Row label="Outstanding" value={formatMoney(data.outstanding)} strong />
              </div>
            </Card>
          </div>

          <Card className="p-5">
            <h2 className="mb-4 text-sm font-semibold uppercase tracking-wider text-muted">Payments</h2>
            {data.payments.length === 0 ? (
              <p className="text-sm text-muted">No payments recorded yet.</p>
            ) : (
              <DataTable columns={paymentColumns} rows={data.payments} pageSize={50} />
            )}
          </Card>

          <PaymentDialog
            open={paying}
            onOpenChange={setPaying}
            outstanding={data.outstanding}
            onPaid={(outstanding) => {
              refresh();
              toast.success(outstanding === 0 ? "Supplier invoice settled." : `Payment recorded — ${formatMoney(outstanding)} still outstanding.`);
            }}
            pay={(request) => recordSupplierPayment(invoiceId, request)}
          />

          <Card className="p-5">
            <h2 className="mb-4 text-sm font-semibold uppercase tracking-wider text-muted">History</h2>
            {isLegacy && (
              <p className="mb-3 text-sm text-muted">
                Imported from the legacy system — anything before the migration lives in the old app.
              </p>
            )}
            <History
              entityType="SupplierInvoice"
              entityId={invoiceId}
              document={{
                docType: "SUPINV",
                docId: invoiceId,
                title: `Supplier invoice ${data.supplierReference ?? ""}`.trim(),
              }}
            />
          </Card>

          <VoidDialog
            open={voiding}
            onOpenChange={setVoiding}
            onVoided={() => {
              refresh();
              toast.success("Supplier invoice voided.");
              router.push("/supplier-invoices");
            }}
            voidInvoice={(reason) => deleteSupplierInvoice(invoiceId, data.rowVersion, reason)}
          />
        </>
      )}
    </FadeIn>
  );
}

function PaymentDialog({ open, onOpenChange, outstanding, onPaid, pay }: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  outstanding: number;
  onPaid: (outstanding: number) => void;
  pay: (request: { amount: number; date: string; method: string | null; reference: string | null }) => Promise<{ outstanding: number }>;
}) {
  const [amount, setAmount] = useState("");
  const [date, setDate] = useState(today);
  const [method, setMethod] = useState("CASH");
  const [reference, setReference] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  const value = Number(amount);
  const valid = Number.isFinite(value) && value > 0 && value <= outstanding;

  async function submit() {
    setSubmitting(true);
    setError(null);
    try {
      const result = await pay({ amount: value, date, method, reference: reference || null });
      onOpenChange(false);
      setAmount("");
      setReference("");
      onPaid(result.outstanding);
    } catch (e) {
      setError(e as ApiError);
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <Dialog
      open={open}
      onOpenChange={onOpenChange}
      title="Record a payment"
      description={`${formatMoney(outstanding)} outstanding. A payment posts to the payables ledger; the invoice settles when nothing is left.`}
      footer={
        <>
          <Button variant="secondary" onClick={() => onOpenChange(false)} disabled={submitting}>Cancel</Button>
          <Button onClick={submit} pending={submitting} disabled={!valid}>Record payment</Button>
        </>
      }
    >
      <div className="space-y-4">
        {error && <ErrorBanner message={error.message} correlationId={error.correlationId} />}
        <Input
          label="Amount"
          inputMode="decimal"
          value={amount}
          onChange={(e) => setAmount(e.target.value)}
          placeholder="0"
          hint={value > outstanding ? `More than the ${formatMoney(outstanding)} outstanding.` : undefined}
        />
        <Input label="Date" type="date" value={date} onChange={(e) => setDate(e.target.value)} />
        <Select label="Method" value={method} onChange={(e) => setMethod(e.target.value)}>
          <option value="CASH">Cash</option>
          <option value="BANK">Bank</option>
          <option value="CHEQUE">Cheque</option>
        </Select>
        <Input label="Reference" value={reference} onChange={(e) => setReference(e.target.value)} />
      </div>
    </Dialog>
  );
}

function VoidDialog({ open, onOpenChange, onVoided, voidInvoice }: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onVoided: () => void;
  voidInvoice: (reason: string) => Promise<unknown>;
}) {
  const [reason, setReason] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  async function submit() {
    setSubmitting(true);
    setError(null);
    try {
      await voidInvoice(reason);
      onOpenChange(false);
      onVoided();
    } catch (e) {
      setError(e as ApiError);
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <Dialog
      open={open}
      onOpenChange={onOpenChange}
      title="Void supplier invoice"
      description="Soft-deleted and audited. The payable is reversed to zero through a compensating ledger entry — its history is kept."
      footer={
        <>
          <Button variant="secondary" onClick={() => onOpenChange(false)} disabled={submitting}>Cancel</Button>
          <Button onClick={submit} pending={submitting} disabled={reason.trim().length < 10}>Void</Button>
        </>
      }
    >
      <div className="space-y-4">
        {error && <ErrorBanner message={error.message} correlationId={error.correlationId} />}
        <Input
          label="Reason"
          value={reason}
          onChange={(e) => setReason(e.target.value)}
          hint="At least 10 characters — recorded on the audit trail."
          placeholder="Why is this supplier invoice being voided?"
        />
      </div>
    </Dialog>
  );
}

function Detail({ label, value, sub }: { label: string; value: string; sub?: string }) {
  return (
    <Card className="p-4">
      <p className="stat-label text-xs font-semibold uppercase tracking-wider">{label}</p>
      <p className="mt-1 font-medium text-text">{value}</p>
      {sub && <p className="text-sm text-muted">{sub}</p>}
    </Card>
  );
}

function Row({ label, value, strong = false }: { label: string; value: string; strong?: boolean }) {
  return (
    <div className="flex items-center justify-between">
      <span className={strong ? "font-semibold text-text" : "text-muted"}>{label}</span>
      <span className={`tabular ${strong ? "text-lg font-bold text-text" : "text-text"}`}>{value}</span>
    </div>
  );
}

const paymentColumns: ColumnDef<SupplierInvoicePaymentLine, unknown>[] = [
  {
    id: "date",
    accessorFn: (row) => row.date,
    header: "Date",
    cell: ({ row }) => <span className="whitespace-nowrap text-muted">{formatReportDate(row.original.date)}</span>,
  },
  {
    id: "amount",
    accessorFn: (row) => row.amount,
    header: "Amount",
    meta: { align: "right" },
    cell: ({ row }) => <span className="tabular font-medium text-text">{row.original.amount > 0 ? formatMoney(row.original.amount) : "—"}</span>,
  },
  {
    id: "method",
    accessorFn: (row) => row.method ?? "",
    header: "Method",
    cell: ({ row }) => <span className="text-text">{row.original.method || "—"}</span>,
  },
  {
    id: "reference",
    accessorFn: (row) => row.reference ?? "",
    header: "Reference",
    cell: ({ row }) => <span className="text-muted">{row.original.reference || "—"}</span>,
  },
];
