"use client";

/**
 * One job card, in full — Phase 6 slice 3.
 *
 * The job as booked: the fault, the customer's serial-tracked equipment, the technician. If it is still
 * PENDING, Close records the cost, sell and completion remarks and flips it to CLOSED — guarded (a second
 * close is refused) and reason-gated. Closing raises no invoice and moves no stock.
 */

import { useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useParams } from "next/navigation";
import Link from "next/link";
import { ArrowLeft, CheckCircle2 } from "lucide-react";
import { ApiError } from "@/lib/api";
import { closeJobCard, getJobCard } from "@/lib/job-cards";
import { me } from "@/lib/auth";
import { PageHeader } from "@/components/shell/app-shell";
import { DataTable, type ColumnDef } from "@/components/data-table";
import { formatMoney, formatReportDate } from "@/components/reports";
import { Badge, Button, Card, Dialog, ErrorBanner, FadeIn, Input, Skeleton, toast } from "@/components/ui";
import type { JobCardLineDetail } from "@/lib/job-cards";

export default function JobCardViewPage() {
  const { id } = useParams<{ id: string }>();
  const jobId = Number(id);
  const queryClient = useQueryClient();

  const job = useQuery({
    queryKey: ["job-card", jobId],
    queryFn: () => getJobCard(jobId),
    enabled: Number.isFinite(jobId),
  });
  const user = useQuery({ queryKey: ["me"], queryFn: me });

  const [closing, setClosing] = useState(false);
  const error = job.error as ApiError | null;
  const data = job.data;
  const isLegacy = data?.origin === "legacy";
  const isClosed = data?.status === "CLOSED";
  // Legacy job cards can be closed too — the legacy app closed them, and a PENDING legacy card is a real
  // open job. Closing is guarded server-side (only PENDING, optimistic concurrency).
  const canClose = data != null && !isClosed && (user.data?.permissions.includes("jobcards") ?? false);

  return (
    <FadeIn className="space-y-6">
      <Link href="/job-cards" className="inline-flex items-center gap-1.5 text-sm text-muted transition-colors hover:text-text">
        <ArrowLeft className="size-4" aria-hidden />
        All job cards
      </Link>

      <div className="flex flex-wrap items-start justify-between gap-4">
        <div className="flex flex-wrap items-center gap-3">
          <PageHeader
            title={data ? `Job card ${data.number}` : "Job card"}
            description={data ? `${data.customerName ?? "—"} · ${formatReportDate(data.date)}` : undefined}
          />
          {isLegacy && <Badge tone="neutral">Legacy</Badge>}
          {data && <Badge tone={isClosed ? "success" : "warning"}>{isClosed ? "Closed" : "Pending"}</Badge>}
        </div>
        {canClose && (
          <Button onClick={() => setClosing(true)}>
            <CheckCircle2 />
            Close job
          </Button>
        )}
      </div>

      {error && <ErrorBanner message={error.message} correlationId={error.correlationId} />}
      {job.isPending && <Skeleton className="h-40" />}

      {data && (
        <>
          <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-4">
            <Detail label="Company" value={data.companyName ?? "—"} />
            <Detail label="Customer" value={data.customerName ?? "—"} sub={data.customerCode ?? undefined} />
            <Detail label="Contact" value={data.contactPerson || "—"} />
            <Detail label="Technician" value={data.technician || "—"} />
          </div>

          <div className="grid gap-4 sm:grid-cols-2">
            <TextCard label="Fault description" value={data.faultDescription} />
            <TextCard label="Remarks" value={data.remarks} />
          </div>

          <Card className="p-5">
            <h2 className="mb-4 text-sm font-semibold uppercase tracking-wider text-muted">Equipment</h2>
            {data.lines.length === 0 ? (
              <p className="text-sm text-muted">No lines recorded.</p>
            ) : (
              <DataTable columns={lineColumns} rows={data.lines} pageSize={50} />
            )}
          </Card>

          {isClosed && (
            <div className="grid gap-4 sm:grid-cols-2">
              <TextCard label="Completion remarks" value={data.completionRemarks} />
              <Card className="space-y-2 p-5">
                <Row label="Cost" value={data.cost != null ? formatMoney(data.cost) : "—"} />
                <Row label="Sell" value={data.sell != null ? formatMoney(data.sell) : "—"} />
                {data.cost != null && data.sell != null && (
                  <div className="border-t border-subtle pt-2">
                    <Row label="Profit" value={formatMoney(data.sell - data.cost)} strong />
                  </div>
                )}
              </Card>
            </div>
          )}

          <CloseDialog
            open={closing}
            onOpenChange={setClosing}
            rowVersion={data.rowVersion}
            onClosed={() => {
              void queryClient.invalidateQueries({ queryKey: ["job-card", jobId] });
              void queryClient.invalidateQueries({ queryKey: ["job-cards"] });
              toast.success(`Job card ${data.number} closed.`);
            }}
            close={(request) => closeJobCard(jobId, request)}
          />
        </>
      )}
    </FadeIn>
  );
}

function CloseDialog({ open, onOpenChange, rowVersion, onClosed, close }: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  rowVersion: number;
  onClosed: () => void;
  close: (request: { expectedRowVersion: number; cost: number; sell: number; completionRemarks: string | null }) => Promise<unknown>;
}) {
  const [cost, setCost] = useState("");
  const [sell, setSell] = useState("");
  const [completion, setCompletion] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  const valid = Number.isFinite(Number(cost)) && Number.isFinite(Number(sell));

  async function submit() {
    setSubmitting(true);
    setError(null);
    try {
      await close({ expectedRowVersion: rowVersion, cost: Number(cost || 0), sell: Number(sell || 0), completionRemarks: completion || null });
      onOpenChange(false);
      onClosed();
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
      title="Close job"
      description="Record what the job cost and what the customer is charged. This flips the card to CLOSED and cannot be undone."
      footer={
        <>
          <Button variant="secondary" onClick={() => onOpenChange(false)} disabled={submitting}>Cancel</Button>
          <Button onClick={submit} pending={submitting} disabled={!valid}>Close job</Button>
        </>
      }
    >
      <div className="space-y-4">
        {error && <ErrorBanner message={error.message} correlationId={error.correlationId} />}
        <div className="grid gap-4 sm:grid-cols-2">
          <Input label="Cost" inputMode="decimal" value={cost} onChange={(e) => setCost(e.target.value)} placeholder="0" />
          <Input label="Sell" inputMode="decimal" value={sell} onChange={(e) => setSell(e.target.value)} placeholder="0" />
        </div>
        <Input label="Completion remarks" value={completion} onChange={(e) => setCompletion(e.target.value)} placeholder="What was done (optional)" />
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

function TextCard({ label, value }: { label: string; value: string | null | undefined }) {
  return (
    <Card className="p-5">
      <p className="stat-label text-xs font-semibold uppercase tracking-wider">{label}</p>
      <p className="mt-2 whitespace-pre-wrap text-sm text-text">{value?.trim() || "—"}</p>
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

const lineColumns: ColumnDef<JobCardLineDetail, unknown>[] = [
  {
    id: "description",
    accessorFn: (row) => row.description ?? "",
    header: "Item / description",
    cell: ({ row }) => <span className="text-text">{row.original.description || "—"}</span>,
  },
  {
    id: "serial",
    accessorFn: (row) => row.serial ?? "",
    header: "Serial no.",
    cell: ({ row }) => <span className="tabular text-muted">{row.original.serial || "—"}</span>,
  },
];
