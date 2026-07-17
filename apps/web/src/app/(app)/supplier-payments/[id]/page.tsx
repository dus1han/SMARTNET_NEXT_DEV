"use client";

/**
 * One supplier payment, in full — Phase 7.
 *
 * Its per-invoice allocations, each a Payment entry on the payables ledger (dual-written to supplier_inv_pay
 * + paymentstat). Void reverses every allocation through compensating entries and reopens the invoices —
 * never by rewriting a balance.
 */

import { useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useParams, useRouter } from "next/navigation";
import Link from "next/link";
import { ArrowLeft, Trash2 } from "lucide-react";
import { ApiError } from "@/lib/api";
import { getSupplierPayment, voidSupplierPayment } from "@/lib/supplier-payments";
import { me } from "@/lib/auth";
import { PageHeader } from "@/components/shell/app-shell";
import { DataTable, type ColumnDef } from "@/components/data-table";
import { formatMoney, formatReportDate } from "@/components/reports";
import { Button, Card, Dialog, ErrorBanner, FadeIn, Input, Skeleton, toast } from "@/components/ui";
import type { SupplierPaymentAllocationLine } from "@/lib/supplier-payments";

export default function SupplierPaymentViewPage() {
  const { id } = useParams<{ id: string }>();
  const paymentId = Number(id);
  const router = useRouter();
  const queryClient = useQueryClient();

  const payment = useQuery({
    queryKey: ["supplier-payment", paymentId],
    queryFn: () => getSupplierPayment(paymentId),
    enabled: Number.isFinite(paymentId),
  });
  const user = useQuery({ queryKey: ["me"], queryFn: me });

  const [voiding, setVoiding] = useState(false);
  const error = payment.error as ApiError | null;
  const data = payment.data;
  const canModify = data != null && (user.data?.permissions.includes("supplier_in") ?? false);

  return (
    <FadeIn className="space-y-6">
      <Link
        href="/supplier-payments"
        className="inline-flex items-center gap-1.5 text-sm text-muted transition-colors hover:text-text"
      >
        <ArrowLeft className="size-4" aria-hidden />
        All supplier payments
      </Link>

      <div className="flex flex-wrap items-start justify-between gap-4">
        <PageHeader
          title={data ? `Payment · ${formatMoney(data.amount)}` : "Payment"}
          description={data ? `${data.supplierName ?? "—"} · ${formatReportDate(data.date)}` : undefined}
        />
        {canModify && (
          <Button variant="secondary" onClick={() => setVoiding(true)}>
            <Trash2 />
            Void
          </Button>
        )}
      </div>

      {error && <ErrorBanner message={error.message} correlationId={error.correlationId} />}

      {payment.isPending && <Skeleton className="h-40" />}

      {data && (
        <>
          <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-4">
            <Detail label="Company" value={data.companyName ?? "—"} />
            <Detail label="Supplier" value={data.supplierName ?? "—"} sub={data.supplierCode ?? undefined} />
            <Detail label="Method" value={data.method || "—"} />
            <Detail label="Reference" value={data.reference || "—"} />
          </div>

          <Card className="p-5">
            <div className="mb-4 flex items-center justify-between">
              <h2 className="text-sm font-semibold uppercase tracking-wider text-muted">Allocations</h2>
              <span className="tabular text-sm text-muted">{formatMoney(data.amount)} across {data.allocations.length} invoice{data.allocations.length === 1 ? "" : "s"}</span>
            </div>
            <DataTable columns={allocationColumns} rows={data.allocations} pageSize={50} />
          </Card>

          <VoidDialog
            open={voiding}
            onOpenChange={setVoiding}
            onVoided={() => {
              void queryClient.invalidateQueries({ queryKey: ["supplier-payment", paymentId] });
              void queryClient.invalidateQueries({ queryKey: ["supplier-payments"] });
              toast.success("Payment voided.");
              router.push("/supplier-payments");
            }}
            voidPayment={(reason) => voidSupplierPayment(paymentId, data.rowVersion, reason)}
          />
        </>
      )}
    </FadeIn>
  );
}

function VoidDialog({ open, onOpenChange, onVoided, voidPayment }: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onVoided: () => void;
  voidPayment: (reason: string) => Promise<unknown>;
}) {
  const [reason, setReason] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  async function submit() {
    setSubmitting(true);
    setError(null);
    try {
      await voidPayment(reason);
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
      title="Void payment"
      description="Soft-deleted and audited. Each allocation is reversed through a compensating ledger entry and its invoice reopens — its history is kept."
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
          placeholder="Why is this payment being voided?"
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

const allocationColumns: ColumnDef<SupplierPaymentAllocationLine, unknown>[] = [
  {
    id: "invoice",
    accessorFn: (row) => row.reference ?? "",
    header: "Invoice",
    cell: ({ row }) => <span className="font-medium text-text">{row.original.reference || `#${row.original.supplierInvoiceId}`}</span>,
  },
  {
    id: "amount",
    accessorFn: (row) => row.amount,
    header: "Amount",
    meta: { align: "right" },
    cell: ({ row }) => <span className="tabular font-medium text-text">{formatMoney(row.original.amount)}</span>,
  },
];
