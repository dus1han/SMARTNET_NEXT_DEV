"use client";

/**
 * One cheque, in full — Phase 7, slice 2.
 *
 * This app's own cheque or a legacy one. Void is soft and reason-gated (not the legacy hard delete), and only
 * for this app's own cheques. Printing is Phase 8.
 */

import { useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useParams, useRouter } from "next/navigation";
import Link from "next/link";
import { ArrowLeft, Trash2 } from "lucide-react";
import { ApiError } from "@/lib/api";
import { getCheque, voidCheque } from "@/lib/cheques";
import { me } from "@/lib/auth";
import { PageHeader } from "@/components/shell/app-shell";
import { History } from "@/components/history/history";
import { formatMoney, formatReportDate } from "@/components/reports";
import { Badge, Button, Card, Dialog, ErrorBanner, FadeIn, Input, Skeleton, toast } from "@/components/ui";

export default function ChequeViewPage() {
  const { id } = useParams<{ id: string }>();
  const chequeId = Number(id);
  const router = useRouter();
  const queryClient = useQueryClient();

  const cheque = useQuery({
    queryKey: ["cheque", chequeId],
    queryFn: () => getCheque(chequeId),
    enabled: Number.isFinite(chequeId),
  });
  const user = useQuery({ queryKey: ["me"], queryFn: me });

  const [voiding, setVoiding] = useState(false);
  const error = cheque.error as ApiError | null;
  const data = cheque.data;
  const isLegacy = data?.origin === "legacy";
  const canModify = data != null && !isLegacy && (user.data?.permissions.includes("cheques") ?? false);

  return (
    <FadeIn className="space-y-6">
      <Link href="/cheques" className="inline-flex items-center gap-1.5 text-sm text-muted transition-colors hover:text-text">
        <ArrowLeft className="size-4" aria-hidden />
        All cheques
      </Link>

      <div className="flex flex-wrap items-start justify-between gap-4">
        <div className="flex flex-wrap items-center gap-3">
          <PageHeader
            title={data ? `Cheque · ${formatMoney(data.amount)}` : "Cheque"}
            description={data ? `${data.payTo} · ${data.chequeDate ? formatReportDate(data.chequeDate) : "—"}` : undefined}
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

      {cheque.isPending && <Skeleton className="h-40" />}

      {data && (
        <>
          <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-3">
            <Detail label="Company" value={data.companyName ?? "—"} />
            <Detail label="Pay to" value={data.payTo || "—"} sub={data.entryType === "Supplier" ? (data.supplierName ?? data.supplierCode ?? undefined) : undefined} />
            <Detail label="Entry" value={data.entryType} />
            <Detail label="Bank" value={data.bank || "—"} />
            <Detail label="Cheque no." value={data.chequeNumber || "—"} />
            <Detail label="Amount" value={formatMoney(data.amount)} />
            <Detail label="Cheque date" value={data.chequeDate ? formatReportDate(data.chequeDate) : "—"} />
            <Detail label="Due date" value={data.dueDate ? formatReportDate(data.dueDate) : "—"} />
          </div>

          <Card className="p-5">
            <h2 className="mb-4 text-sm font-semibold uppercase tracking-wider text-muted">History</h2>
            {isLegacy && (
              <p className="mb-3 text-sm text-muted">
                Imported from the legacy system — anything before the migration lives in the old app.
              </p>
            )}
            <History entityType="Cheque" entityId={chequeId} />
          </Card>

          <VoidDialog
            open={voiding}
            onOpenChange={setVoiding}
            onVoided={() => {
              void queryClient.invalidateQueries({ queryKey: ["cheque", chequeId] });
              void queryClient.invalidateQueries({ queryKey: ["cheques"] });
              toast.success("Cheque voided.");
              router.push("/cheques");
            }}
            voidCheque={(reason) => voidCheque(chequeId, data.rowVersion, reason)}
          />
        </>
      )}
    </FadeIn>
  );
}

function VoidDialog({ open, onOpenChange, onVoided, voidCheque: voidIt }: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onVoided: () => void;
  voidCheque: (reason: string) => Promise<unknown>;
}) {
  const [reason, setReason] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  async function submit() {
    setSubmitting(true);
    setError(null);
    try {
      await voidIt(reason);
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
      title="Void cheque"
      description="Soft-deleted and audited — its history is kept (the legacy delete removed the row)."
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
          placeholder="Why is this cheque being voided?"
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
