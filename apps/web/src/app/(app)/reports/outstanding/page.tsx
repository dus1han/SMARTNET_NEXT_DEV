"use client";

/**
 * Customer outstanding report (customer_outstanding) — and the gated bulk-dunning action.
 *
 * It reads the legacy `balance` column as-is so the figure matches the statements the business already
 * sends — but that column is wrong by Rs 1.55M (Finding 1: invoices with negative balances the legacy
 * `balance > 0` filter drops). So a customer holding such an invoice is flagged: the number is shown
 * AND marked, never presented as clean.
 *
 * "Send statements" queues an email per selected customer and returns at once (the legacy version was a
 * synchronous 16-minute loop). But sending is gated by the company mail kill switch, off by default —
 * so today it queues and logs, and sends nothing. That is deliberate: emailing 223 customers a balance
 * known to be wrong is a business decision, made after remediation, not a button that does it quietly.
 */

import { useMutation, useQuery } from "@tanstack/react-query";
import { AlertTriangle, Clock, Download, Mail, Wallet } from "lucide-react";
import { useState } from "react";
import { ApiError } from "@/lib/api";
import {
  getOutstandingReport,
  outstandingDetailExportUrl,
  outstandingReportExportUrl,
  sendDunning,
  statementRecipients,
  type CompanyFilter,
  type OutstandingRow,
} from "@/lib/reports";
import { today } from "@/lib/period";
import { PageHeader } from "@/components/shell/app-shell";
import { DataTable, downloadExcel, type ColumnDef } from "@/components/data-table";
import { ReportFilterBar, StatTile, formatMoney, formatReportDate } from "@/components/reports";
import { AnimatedNumber, Badge, Button, Checkbox, Dialog, ErrorBanner, FadeIn, Input, Skeleton, toast } from "@/components/ui";

export default function OutstandingReportPage() {
  const [company, setCompany] = useState<CompanyFilter>("all");
  const [asAt, setAsAt] = useState(today);
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [confirming, setConfirming] = useState(false);
  const [exportingSelected, setExportingSelected] = useState(false);

  // Today is the live figure; a past date rolls the balances back to what was owed then.
  const todayStr = today();
  const isHistorical = asAt < todayStr;

  const report = useQuery({
    queryKey: ["outstanding-report", company, asAt],
    queryFn: () => getOutstandingReport(company, asAt),
  });

  // Changing company changes which customers exist, so a carried-over selection would be stale —
  // clear it as part of the switch rather than reacting to it in an effect.
  const changeCompany = (next: CompanyFilter) => {
    setCompany(next);
    setSelected(new Set());
  };

  const data = report.data;
  const loadError = report.error as ApiError | null;
  const rows = data?.rows ?? [];

  const allSelected = rows.length > 0 && rows.every((r) => selected.has(r.customerCode));

  const toggle = (code: string) =>
    setSelected((prev) => {
      const next = new Set(prev);
      if (next.has(code)) next.delete(code);
      else next.add(code);
      return next;
    });

  const toggleAll = () =>
    setSelected(allSelected ? new Set() : new Set(rows.map((r) => r.customerCode)));

  // With exactly one customer selected there is a single contact list to choose from, so the dialog
  // offers it — the same choice the job sheet gives. A bulk run has no one list; each customer there
  // falls back to their own address on file, and the server enforces that rule too.
  const singleCustomer = selected.size === 1 ? [...selected][0] : null;

  const recipients = useQuery({
    queryKey: ["statement-recipients", singleCustomer],
    queryFn: () => statementRecipients(singleCustomer!),
    enabled: confirming && singleCustomer !== null,
  });

  const [pickedContacts, setPickedContacts] = useState<Set<number> | null>(null);

  const contacts = recipients.data?.contacts ?? [];
  // Until the user touches anything, the selection is the server's default — the document contacts.
  const chosenContacts = pickedContacts ?? new Set(contacts.filter((c) => c.selected).map((c) => c.id));

  const toggleContact = (id: number) => {
    const next = new Set(chosenContacts);
    if (next.has(id)) next.delete(id);
    else next.add(id);
    setPickedContacts(next);
  };

  const send = useMutation({
    mutationFn: () =>
      sendDunning([...selected], singleCustomer !== null ? [...chosenContacts] : undefined),
    onSuccess: (result) => {
      toast[result.sendEnabled ? "success" : "info"](result.message);
      setSelected(new Set());
      setPickedContacts(null);
      setConfirming(false);
    },
    onError: (error: unknown) => toast.error(error instanceof ApiError ? error.message : "That did not work."),
  });

  const columns: ColumnDef<OutstandingRow, unknown>[] = [
    {
      id: "select",
      enableSorting: false,
      header: () => (
        <input
          type="checkbox"
          checked={allSelected}
          onChange={toggleAll}
          aria-label="Select all customers"
          className="size-4 cursor-pointer rounded border-strong accent-primary"
        />
      ),
      cell: ({ row }) => (
        <input
          type="checkbox"
          checked={selected.has(row.original.customerCode)}
          onChange={() => toggle(row.original.customerCode)}
          aria-label={`Select ${row.original.customerName}`}
          className="size-4 cursor-pointer rounded border-strong accent-primary"
        />
      ),
    },
    ...baseColumns,
  ];

  return (
    <FadeIn className="space-y-6">
      <PageHeader
        title="Customer outstanding"
        description="What customers owe, aged from the invoice date, for the company you are working in."
      />

      <ReportFilterBar company={company} onCompany={changeCompany} showDates={false}>
        <Input
          label="As at"
          type="date"
          value={asAt}
          max={todayStr}
          onChange={(e) => setAsAt(e.target.value || todayStr)}
          className="w-44"
        />
      </ReportFilterBar>

      {isHistorical && (
        <p className="-mt-2 text-sm text-muted">
          Showing what was owed on {data ? formatReportDate(data.asAt) : formatReportDate(asAt)} — later
          payments added back, invoices raised after excluded.
        </p>
      )}

      {loadError && <ErrorBanner message={loadError.message} correlationId={loadError.correlationId} />}

      <div className="grid gap-4 sm:grid-cols-3">
        <StatTile
          label="Total outstanding"
          icon={Wallet}
          color="indigo"
          value={data ? <AnimatedNumber value={data.totalOutstanding} format={formatMoney} /> : "—"}
          sub={data ? `${data.customerCount} customer${data.customerCount === 1 ? "" : "s"}` : undefined}
        />
        <StatTile
          label="Current (0–30)"
          icon={Clock}
          color="emerald"
          delayMs={70}
          value={data ? <AnimatedNumber value={data.totalCurrent} format={formatMoney} /> : "—"}
        />
        <StatTile
          label="Over 90 days"
          icon={Clock}
          color="rose"
          delayMs={140}
          value={data ? <AnimatedNumber value={data.total90} format={formatMoney} /> : "—"}
        />
      </div>

      {data && data.defectCount > 0 && (
        <p className="flex items-center gap-2 text-sm text-warning-text">
          <AlertTriangle className="size-4" aria-hidden />
          {data.defectCount} customer{data.defectCount === 1 ? " has an invoice" : "s have invoices"} with
          a negative balance (Finding 1). Their outstanding is overstated — shown as the business sends
          it, and flagged. The data-remediation phase corrects it.
        </p>
      )}

      <DataTable
        columns={columns}
        rows={data?.rows}
        loading={report.isPending}
        defaultSort={{ id: "outstanding", desc: true }}
        searchable={(r) => `${r.customerName} ${r.customerCode}`}
        searchPlaceholder="Search customers…"
        exportUrl={outstandingReportExportUrl(company, asAt)}
        exportFilename={`outstanding-${asAt}.xlsx`}
        actions={
          <>
            <Button size="sm" disabled={selected.size === 0} onClick={() => setConfirming(true)}>
              <Mail />
              Send statements{selected.size > 0 ? ` (${selected.size})` : ""}
            </Button>

            <Button
              variant="secondary"
              size="sm"
              disabled={selected.size === 0}
              pending={exportingSelected}
              onClick={async () => {
                setExportingSelected(true);
                try {
                  await downloadExcel(outstandingDetailExportUrl(company, [...selected], asAt), `outstanding-detail-${asAt}.xlsx`);
                } catch {
                  toast.error("The export failed.");
                } finally {
                  setExportingSelected(false);
                }
              }}
            >
              <Download />
              Export selected{selected.size > 0 ? ` (${selected.size})` : ""}
            </Button>
          </>
        }
        empty={{ title: "Nothing outstanding", description: "No customer has an unpaid balance." }}
      />

      <Dialog
        open={confirming}
        onOpenChange={(next) => !next && setConfirming(false)}
        title="Send outstanding statements"
        description={`${selected.size} customer${selected.size === 1 ? "" : "s"} selected.`}
        footer={
          <>
            <Button variant="ghost" onClick={() => setConfirming(false)}>
              Cancel
            </Button>
            <Button
              pending={send.isPending}
              onClick={() => send.mutate()}
              // With one customer chosen, a send with nobody ticked would go nowhere.
              disabled={singleCustomer !== null && chosenContacts.size === 0}
            >
              Queue statements
            </Button>
          </>
        }
      >
        <div className="space-y-3 text-sm text-muted">
          {singleCustomer !== null && (
            <div className="space-y-2">
              <p className="stat-label text-xs font-semibold uppercase tracking-wider">Send to</p>
              {recipients.isPending && <Skeleton className="h-16" />}
              {recipients.data?.blocked && (
                <p className="rounded-md bg-warning/10 p-3 text-sm text-warning">{recipients.data.blocked}</p>
              )}
              {contacts.map((contact) => (
                <Checkbox
                  key={contact.id}
                  checked={chosenContacts.has(contact.id)}
                  onChange={() => toggleContact(contact.id)}
                  label={contact.name?.trim() ? `${contact.name} — ${contact.email}` : contact.email}
                  hint={contact.usage === "NotificationsOnly" ? "Notifications only" : undefined}
                />
              ))}
            </div>
          )}

          <p>
            {singleCustomer !== null
              ? "The statement is queued to the contacts you chose and the request returns at once — nothing blocks while mail is sent."
              : "Each selected customer is queued an outstanding statement and the request returns at once — nothing blocks while mail is sent. Each goes to the address on file; choose a single customer to pick their contacts."}
          </p>
          <p className="flex items-start gap-2 rounded-lg border border-subtle bg-surface-sunken p-3 text-warning-text">
            <AlertTriangle className="mt-0.5 size-4 shrink-0" aria-hidden />
            <span>
              Sending is switched off by default, so this <b>queues and logs but sends nothing</b>. The
              outstanding figure is known to be wrong (Finding 1); enabling the send is a business
              decision made after the balances are corrected.
            </span>
          </p>
        </div>
      </Dialog>
    </FadeIn>
  );
}

const mutedMoney = (v: number) =>
  v > 0 ? <span className="tabular text-muted">{formatMoney(v)}</span> : <span className="text-muted">—</span>;

const baseColumns: ColumnDef<OutstandingRow, unknown>[] = [
  {
    id: "customer",
    header: "Customer",
    accessorFn: (row) => row.customerName,
    cell: ({ row }) => {
      const r = row.original;
      return (
        <div className="flex items-center gap-2">
          <div className="min-w-0">
            <p className="truncate text-text">{r.customerName || <span className="text-muted">—</span>}</p>
            <p className="truncate text-xs text-muted">{r.customerCode}</p>
          </div>
          {r.hasDefect && (
            <Badge
              tone="warning"
              title="This customer has an invoice with a negative balance (Finding 1). The outstanding shown is overstated."
            >
              Defect
            </Badge>
          )}
        </div>
      );
    },
  },
  {
    id: "outstanding",
    header: "Outstanding",
    accessorFn: (row) => row.outstanding,
    meta: { align: "right" },
    cell: ({ row }) => <span className="tabular font-medium text-text">{formatMoney(row.original.outstanding)}</span>,
  },
  {
    id: "current",
    header: "Current",
    accessorFn: (row) => row.current,
    meta: { align: "right" },
    cell: ({ row }) => mutedMoney(row.original.current),
  },
  {
    id: "d30",
    header: "31–60",
    accessorFn: (row) => row.days30,
    meta: { align: "right" },
    cell: ({ row }) => mutedMoney(row.original.days30),
  },
  {
    id: "d60",
    header: "61–90",
    accessorFn: (row) => row.days60,
    meta: { align: "right" },
    cell: ({ row }) => mutedMoney(row.original.days60),
  },
  {
    id: "d90",
    header: "90+",
    accessorFn: (row) => row.days90,
    meta: { align: "right" },
    cell: ({ row }) =>
      row.original.days90 > 0 ? (
        <span className="tabular font-medium text-warning-text">{formatMoney(row.original.days90)}</span>
      ) : (
        <span className="text-muted">—</span>
      ),
  },
];
