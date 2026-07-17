"use client";

/**
 * One customer receipt, in full — Phase 7 slice 1.
 *
 * Its per-invoice allocations, each a Payment entry on the receivables ledger (dual-written to the legacy
 * payments row + invoice_h.balance). Void reverses every allocation through compensating entries — never by
 * rewriting a balance (the legacy deletepay hard-deleted the row and added the amount back in place).
 */

import { useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useParams, useRouter } from "next/navigation";
import Link from "next/link";
import { ArrowLeft, Trash2 } from "lucide-react";
import { ApiError } from "@/lib/api";
import { getCustomerReceipt, voidCustomerReceipt } from "@/lib/payments";
import { me } from "@/lib/auth";
import { PageHeader } from "@/components/shell/app-shell";
import { DataTable, type ColumnDef } from "@/components/data-table";
import { formatMoney, formatReportDate } from "@/components/reports";
import { Badge, Button, Card, Dialog, ErrorBanner, FadeIn, Input, Skeleton, toast } from "@/components/ui";
import type { ReceiptAllocationLine } from "@/lib/payments";

export default function CustomerReceiptViewPage() {
  const { id } = useParams<{ id: string }>();
  const receiptId = Number(id);
  const router = useRouter();
  const queryClient = useQueryClient();

  const receipt = useQuery({
    queryKey: ["customer-receipt", receiptId],
    queryFn: () => getCustomerReceipt(receiptId),
    enabled: Number.isFinite(receiptId),
  });
  const user = useQuery({ queryKey: ["me"], queryFn: me });

  const [voiding, setVoiding] = useState(false);
  const error = receipt.error as ApiError | null;
  const data = receipt.data;
  const isLegacy = data?.origin === "legacy";
  const canModify = data != null && !isLegacy && (user.data?.permissions.includes("payments") ?? false);

  return (
    <FadeIn className="space-y-6">
      <Link
        href="/payments"
        className="inline-flex items-center gap-1.5 text-sm text-muted transition-colors hover:text-text"
      >
        <ArrowLeft className="size-4" aria-hidden />
        All receipts
      </Link>

      <div className="flex flex-wrap items-start justify-between gap-4">
        <div className="flex flex-wrap items-center gap-3">
          <PageHeader
            title={data ? `Receipt · ${formatMoney(data.amount)}` : "Receipt"}
            description={data ? `${data.customerName ?? "—"} · ${formatReportDate(data.date)}` : undefined}
          />
          {isLegacy && <Badge tone="neutral">Legacy</Badge>}
        </div>
        {canModify && (
          <Button variant="secondary" onClick={() => setVoiding(true)}>
            <Trash2 />
            Void
          </Button>
        )}
      </div>

      {error && <ErrorBanner message={error.message} correlationId={error.correlationId} />}

      {receipt.isPending && <Skeleton className="h-40" />}

      {data && (
        <>
          <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-4">
            <Detail label="Company" value={data.companyName ?? "—"} />
            <Detail label="Customer" value={data.customerName ?? "—"} sub={data.customerCode ?? undefined} />
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
              void queryClient.invalidateQueries({ queryKey: ["customer-receipt", receiptId] });
              void queryClient.invalidateQueries({ queryKey: ["customer-receipts"] });
              toast.success("Receipt voided.");
              router.push("/payments");
            }}
            voidReceipt={(reason) => voidCustomerReceipt(receiptId, data.rowVersion, reason)}
          />
        </>
      )}
    </FadeIn>
  );
}

function VoidDialog({ open, onOpenChange, onVoided, voidReceipt }: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onVoided: () => void;
  voidReceipt: (reason: string) => Promise<unknown>;
}) {
  const [reason, setReason] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  async function submit() {
    setSubmitting(true);
    setError(null);
    try {
      await voidReceipt(reason);
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
      title="Void receipt"
      description="Soft-deleted and audited. Each allocation is reversed through a compensating ledger entry, and the legacy balance is restored — its history is kept."
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
          placeholder="Why is this receipt being voided?"
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

const allocationColumns: ColumnDef<ReceiptAllocationLine, unknown>[] = [
  {
    id: "invoice",
    accessorFn: (row) => row.invoiceNumber ?? "",
    header: "Invoice",
    cell: ({ row }) => <span className="font-medium text-text">{row.original.invoiceNumber || `#${row.original.invoiceId}`}</span>,
  },
  {
    id: "amount",
    accessorFn: (row) => row.amount,
    header: "Amount",
    meta: { align: "right" },
    cell: ({ row }) => <span className="tabular font-medium text-text">{formatMoney(row.original.amount)}</span>,
  },
];
