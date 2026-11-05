"use client";

/**
 * The suspended account.
 *
 * Outstanding moved off an invoice — new or legacy — so it shows nowhere an outstanding figure does: not
 * the invoice, the dashboard, the reports, dunning or the customer's open invoices. The customer still owes
 * it (the credit limit still counts it), so this screen is where it is kept in sight.
 *
 * Each row comes back one of two ways: Reinstate puts the amount back on the invoice, to be collected the
 * ordinary way; Receive payment puts it back and records the receipt in the same act. The History tab is
 * what has come back off the account.
 */

import { useMemo, useState, type ReactNode } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { RotateCcw, Wallet } from "lucide-react";
import { ApiError } from "@/lib/api";
import { cn } from "@/lib/cn";
import { today } from "@/lib/period";
import {
  getSuspendedReceivables,
  reinstateSuspension,
  settleSuspension,
  type SuspendedReceivableSummary,
} from "@/lib/suspended-receivables";
import { PageHeader } from "@/components/shell/app-shell";
import { DataTable, type ColumnDef } from "@/components/data-table";
import { formatMoney, formatReportDate } from "@/components/reports";
import { Badge, Button, Card, Dialog, ErrorBanner, FadeIn, Input, Select, toast } from "@/components/ui";

export default function SuspendedReceivablesPage() {
  const queryClient = useQueryClient();
  const [active, setActive] = useState(true);
  const [reinstating, setReinstating] = useState<SuspendedReceivableSummary | null>(null);
  const [settling, setSettling] = useState<SuspendedReceivableSummary | null>(null);

  const list = useQuery({
    queryKey: ["suspended-receivables", active],
    queryFn: () => getSuspendedReceivables(active),
  });
  const error = list.error as ApiError | null;
  const rows = list.data ?? [];
  const total = rows.reduce((sum, r) => sum + r.amount, 0);
  const customers = new Set(rows.map((r) => r.customerId)).size;

  function refresh(invoiceId: number) {
    void queryClient.invalidateQueries({ queryKey: ["suspended-receivables"] });
    void queryClient.invalidateQueries({ queryKey: ["invoice", invoiceId] });
    void queryClient.invalidateQueries({ queryKey: ["invoices"] });
    void queryClient.invalidateQueries({ queryKey: ["customer-receipts"] });
  }

  const columns = useMemo(
    () => buildColumns(active, { onReinstate: setReinstating, onSettle: setSettling }),
    [active],
  );

  return (
    <FadeIn className="space-y-6">
      <PageHeader
        title="Suspended account"
        description="Outstanding moved off its invoice — hidden from every outstanding figure, but still owed. Bring it back when the customer pays."
      />

      <div className="flex w-fit items-center gap-1 rounded-lg border border-subtle bg-surface-sunken p-1">
        <Tab active={active} onClick={() => setActive(true)}>Suspended</Tab>
        <Tab active={!active} onClick={() => setActive(false)}>History</Tab>
      </div>

      {active && (
        <div className="grid gap-4 sm:grid-cols-3">
          <Stat label="On the suspended account" value={formatMoney(total)} strong />
          <Stat label="Invoices" value={String(rows.length)} />
          <Stat label="Customers" value={String(customers)} />
        </div>
      )}

      {error && <ErrorBanner message={error.message} correlationId={error.correlationId} />}

      <DataTable
        columns={columns}
        rows={list.data}
        loading={list.isPending}
        pageSize={50}
        searchable={(row) => `${row.invoiceNumber} ${row.customerName ?? ""} ${row.customerCode ?? ""} ${row.reason}`}
        searchPlaceholder="Search by invoice, customer or reason…"
        defaultSort={{ id: "suspendedOn", desc: true }}
        empty={
          active
            ? {
                title: "Nothing on the suspended account",
                description: "Open an invoice with an outstanding balance and choose Suspend to move it here.",
              }
            : { title: "Nothing has come back yet", description: "Reinstated and settled amounts are kept here for the record." }
        }
      />

      {reinstating && (
        <ReinstateDialog
          row={reinstating}
          onOpenChange={(open) => !open && setReinstating(null)}
          onDone={() => {
            refresh(reinstating.invoiceId);
            toast.success(`${formatMoney(reinstating.amount)} is back on invoice ${reinstating.invoiceNumber}.`);
            setReinstating(null);
          }}
        />
      )}

      {settling && (
        <SettleDialog
          row={settling}
          onOpenChange={(open) => !open && setSettling(null)}
          onDone={(paid, alreadyExisted) => {
            refresh(settling.invoiceId);
            toast.success(
              alreadyExisted
                ? "That payment was already recorded."
                : `Payment of ${formatMoney(paid)} recorded against invoice ${settling.invoiceNumber}.`,
            );
            setSettling(null);
          }}
        />
      )}
    </FadeIn>
  );
}

function buildColumns(
  active: boolean,
  actions: { onReinstate: (row: SuspendedReceivableSummary) => void; onSettle: (row: SuspendedReceivableSummary) => void },
): ColumnDef<SuspendedReceivableSummary, unknown>[] {
  const columns: ColumnDef<SuspendedReceivableSummary, unknown>[] = [
    {
      id: "suspendedOn",
      accessorFn: (row) => row.suspendedOn,
      header: "Suspended",
      cell: ({ row }) => (
        <span className="whitespace-nowrap text-muted">
          {formatReportDate(row.original.suspendedOn)}
          {active && <span className="block text-xs">{daysSince(row.original.suspendedOn)}</span>}
        </span>
      ),
    },
    {
      id: "invoice",
      accessorFn: (row) => row.invoiceNumber,
      header: "Invoice",
      cell: ({ row }) => (
        <span className="flex items-center gap-2 whitespace-nowrap">
          <Link
            href={`/invoices/${row.original.invoiceId}`}
            className="font-medium text-primary hover:underline"
            onClick={(e) => e.stopPropagation()}
          >
            {row.original.invoiceNumber}
          </Link>
          {row.original.origin === "legacy" && <Badge tone="neutral">Legacy</Badge>}
        </span>
      ),
    },
    {
      id: "customer",
      accessorFn: (row) => row.customerName ?? "",
      header: "Customer",
      cell: ({ row }) => (
        <span className="text-text">
          {row.original.customerName ?? "—"}
          {row.original.companyName && <span className="block text-xs text-muted">{row.original.companyName}</span>}
        </span>
      ),
    },
    {
      id: "reason",
      accessorFn: (row) => row.reason,
      header: "Reason",
      cell: ({ row }) => (
        <span className="text-sm text-muted">
          {row.original.reason}
          {row.original.suspendedBy && <span className="block text-xs">by {row.original.suspendedBy}</span>}
        </span>
      ),
    },
    {
      id: "amount",
      accessorFn: (row) => row.amount,
      header: "Amount",
      meta: { align: "right" },
      cell: ({ row }) => (
        <span className="tabular font-medium text-text">
          {formatMoney(row.original.amount)}
          <span className="block text-xs font-normal text-muted">of {formatMoney(row.original.invoiceTotal)}</span>
        </span>
      ),
    },
  ];

  if (active) {
    columns.push({
      id: "actions",
      header: "",
      enableSorting: false,
      cell: ({ row }) => (
        <span className="flex justify-end gap-2">
          <Button size="sm" variant="secondary" onClick={() => actions.onReinstate(row.original)}>
            <RotateCcw />
            Reinstate
          </Button>
          <Button size="sm" onClick={() => actions.onSettle(row.original)}>
            <Wallet />
            Receive payment
          </Button>
        </span>
      ),
    });
  } else {
    columns.push({
      id: "status",
      accessorFn: (row) => row.status,
      header: "Outcome",
      cell: ({ row }) => (
        <span className="whitespace-nowrap">
          <Badge tone={row.original.status === "Settled" ? "success" : "neutral"}>{row.original.status}</Badge>
          {row.original.reinstatedAt && (
            <span className="block text-xs text-muted">{formatReportDate(row.original.reinstatedAt.slice(0, 10))}</span>
          )}
          {row.original.receiptId != null && (
            <Link href={`/payments/${row.original.receiptId}`} className="block text-xs text-primary hover:underline">
              View receipt
            </Link>
          )}
        </span>
      ),
    });
  }

  return columns;
}

function ReinstateDialog({ row, onOpenChange, onDone }: {
  row: SuspendedReceivableSummary;
  onOpenChange: (open: boolean) => void;
  onDone: () => void;
}) {
  const [note, setNote] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  async function submit() {
    setSubmitting(true);
    setError(null);
    try {
      await reinstateSuspension(row.id, { rowVersion: row.rowVersion, note: note || null });
      onDone();
    } catch (e) {
      setError(e as ApiError);
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <Dialog
      open
      onOpenChange={onOpenChange}
      title={`Reinstate invoice ${row.invoiceNumber}`}
      description={`${formatMoney(row.amount)} goes back on the invoice's outstanding, where it shows and is collected the ordinary way.`}
      footer={
        <>
          <Button variant="secondary" onClick={() => onOpenChange(false)} disabled={submitting}>Cancel</Button>
          <Button onClick={submit} pending={submitting}>Reinstate</Button>
        </>
      }
    >
      <div className="space-y-4">
        {error && <ErrorBanner message={error.message} correlationId={error.correlationId} />}
        <Input label="Note (optional)" value={note} onChange={(e) => setNote(e.target.value)} placeholder="Dispute resolved…" />
      </div>
    </Dialog>
  );
}

function SettleDialog({ row, onOpenChange, onDone }: {
  row: SuspendedReceivableSummary;
  onOpenChange: (open: boolean) => void;
  onDone: (paid: number, alreadyExisted: boolean) => void;
}) {
  const [amount, setAmount] = useState(String(row.amount));
  const [date, setDate] = useState(today);
  const [method, setMethod] = useState("BANK");
  const [reference, setReference] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  // Minted once per dialog — a resubmit of the same payment is deduped rather than taking it twice.
  const [idempotencyKey] = useState(() => crypto.randomUUID());

  const value = Number(amount);
  const valid = Number.isFinite(value) && value > 0;
  const short = valid && value < row.amount ? row.amount - value : 0;

  async function submit() {
    setSubmitting(true);
    setError(null);
    try {
      const created = await settleSuspension(row.id, {
        rowVersion: row.rowVersion,
        amount: value,
        date,
        method,
        reference: reference || null,
        idempotencyKey,
      });
      onDone(created.amount, created.alreadyExisted);
    } catch (e) {
      setError(e as ApiError);
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <Dialog
      open
      onOpenChange={onOpenChange}
      title={`Receive payment — invoice ${row.invoiceNumber}`}
      description={`${formatMoney(row.amount)} is brought back onto the invoice and the payment recorded against it, in one step.`}
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
          label="Amount received"
          inputMode="decimal"
          value={amount}
          onChange={(e) => setAmount(e.target.value)}
          hint={short > 0 ? `${formatMoney(short)} will stay on the invoice as ordinary outstanding.` : undefined}
        />
        <div className="grid gap-4 sm:grid-cols-2">
          <Input label="Date" type="date" value={date} onChange={(e) => setDate(e.target.value)} />
          <Select label="Method" value={method} onChange={(e) => setMethod(e.target.value)}>
            <option value="CASH">Cash</option>
            <option value="BANK">Bank</option>
            <option value="CHEQUE">Cheque</option>
            <option value="ONLINE">Online</option>
          </Select>
        </div>
        <Input
          label="Reference"
          value={reference}
          onChange={(e) => setReference(e.target.value)}
          placeholder="Cheque no., transfer ref…"
        />
      </div>
    </Dialog>
  );
}

function daysSince(date: string): string {
  const days = Math.floor((Date.parse(today()) - Date.parse(date)) / 86_400_000);
  return days <= 0 ? "today" : days === 1 ? "1 day" : `${days} days`;
}

function Stat({ label, value, strong = false }: { label: string; value: string; strong?: boolean }) {
  return (
    <Card className="p-4">
      <p className="stat-label text-xs font-semibold uppercase tracking-wider">{label}</p>
      <p className={cn("mt-1 tabular text-text", strong ? "text-2xl font-bold" : "text-lg font-semibold")}>{value}</p>
    </Card>
  );
}

function Tab({ active, onClick, children }: { active: boolean; onClick: () => void; children: ReactNode }) {
  return (
    <button
      type="button"
      onClick={onClick}
      aria-pressed={active}
      className={cn(
        "rounded-md px-3 py-1.5 text-sm font-medium transition-colors",
        active ? "bg-surface text-text shadow-xs" : "text-muted hover:text-text",
      )}
    >
      {children}
    </button>
  );
}
