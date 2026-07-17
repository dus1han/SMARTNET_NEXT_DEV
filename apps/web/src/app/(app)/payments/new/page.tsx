"use client";

/**
 * Record a customer receipt — Phase 7 slice 1.
 *
 * Money received, allocated across the customer's open invoices. Pick a customer, and its outstanding
 * invoices (new and legacy alike, derived from the receivables ledger) appear to allocate against. Each
 * allocation posts a Payment entry to the ledger and dual-writes the legacy shadow. An idempotency key
 * (minted once per form) makes a double-submit return the first receipt rather than take the money twice.
 */

import { useMemo, useState } from "react";
import { useRouter } from "next/navigation";
import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { ArrowLeft } from "lucide-react";
import { ApiError } from "@/lib/api";
import { createCustomerReceipt, getOutstandingInvoices } from "@/lib/payments";
import { listCompanies, listCustomers } from "@/lib/customers";
import { today } from "@/lib/period";
import { PageHeader } from "@/components/shell/app-shell";
import { CustomerCombobox } from "@/components/documents/line-draft";
import { formatMoney, formatReportDate } from "@/components/reports";
import { Badge, Button, Card, ErrorBanner, FadeIn, Input, Select, toast } from "@/components/ui";

export default function NewCustomerReceiptPage() {
  const router = useRouter();
  const companies = useQuery({ queryKey: ["companies"], queryFn: listCompanies });
  const customers = useQuery({ queryKey: ["customers"], queryFn: listCustomers });

  const [companyId, setCompanyId] = useState("");
  const [customerId, setCustomerId] = useState("");
  const [date, setDate] = useState(today);
  const [method, setMethod] = useState("CASH");
  const [reference, setReference] = useState("");
  const [allocations, setAllocations] = useState<Record<number, string>>({});
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  // Minted once per form instance — a resubmit of the same receipt carries the same key and is deduped.
  const [idempotencyKey] = useState(() => crypto.randomUUID());

  const customerKey = customerId === "" ? null : Number(customerId);
  const outstanding = useQuery({
    queryKey: ["outstanding-invoices", customerKey],
    queryFn: () => getOutstandingInvoices(customerKey!),
    enabled: customerKey != null,
  });

  const invoices = useMemo(() => outstanding.data ?? [], [outstanding.data]);

  // The total is the sum of the per-invoice allocations, and each must stay within that invoice's outstanding.
  const { total, overAllocated, positiveCount } = useMemo(() => {
    let sum = 0;
    let over = false;
    let count = 0;
    for (const inv of invoices) {
      const raw = allocations[inv.invoiceId];
      const value = raw ? Number(raw) : 0;
      if (Number.isFinite(value) && value > 0) {
        sum += value;
        count += 1;
        if (value > inv.outstanding + 1e-9) over = true;
      }
    }
    return { total: Math.round(sum * 100) / 100, overAllocated: over, positiveCount: count };
  }, [invoices, allocations]);

  const canSubmit = companyId !== "" && customerKey != null && positiveCount > 0 && !overAllocated && !submitting;

  function setAllocation(invoiceId: number, value: string) {
    setAllocations((prev) => ({ ...prev, [invoiceId]: value }));
  }

  function resetCustomer(id: string) {
    setCustomerId(id);
    setAllocations({});
  }

  async function submit() {
    setSubmitting(true);
    setError(null);
    try {
      const lines = invoices
        .map((inv) => ({ invoiceId: inv.invoiceId, amount: allocations[inv.invoiceId] ? Number(allocations[inv.invoiceId]) : 0 }))
        .filter((a) => a.amount > 0);

      const created = await createCustomerReceipt({
        companyId: Number(companyId),
        customerId: customerKey!,
        date,
        method: method || null,
        reference: reference || null,
        idempotencyKey,
        allocations: lines,
      });
      toast.success(
        created.alreadyExisted
          ? "That receipt was already recorded."
          : `Receipt recorded — ${formatMoney(created.amount)} across ${lines.length} invoice${lines.length === 1 ? "" : "s"}.`,
      );
      router.push(`/payments/${created.id}`);
    } catch (e) {
      setError(e as ApiError);
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <FadeIn className="space-y-6">
      <Link
        href="/payments"
        className="inline-flex items-center gap-1.5 text-sm text-muted transition-colors hover:text-text"
      >
        <ArrowLeft className="size-4" aria-hidden />
        All receipts
      </Link>

      <PageHeader
        title="Record a receipt"
        description="Money received from a customer, allocated across its open invoices. Each allocation settles against the receivables ledger."
      />

      {error && <ErrorBanner message={error.message} correlationId={error.correlationId} />}

      <Card className="grid gap-4 p-5 sm:grid-cols-2 lg:grid-cols-4">
        <Select label="Company" value={companyId} onChange={(e) => setCompanyId(e.target.value)}>
          <option value="">Select…</option>
          {companies.data?.map((c) => (
            <option key={c.id} value={c.id}>{c.name}</option>
          ))}
        </Select>

        <CustomerCombobox customers={customers.data ?? []} value={customerId} onChange={resetCustomer} />

        <Input label="Date" type="date" value={date} onChange={(e) => setDate(e.target.value)} />

        <Select label="Method" value={method} onChange={(e) => setMethod(e.target.value)}>
          <option value="CASH">Cash</option>
          <option value="BANK">Bank</option>
          <option value="CHEQUE">Cheque</option>
          <option value="ONLINE">Online</option>
        </Select>
      </Card>

      <Card className="p-5">
        <div className="mb-4 flex items-center justify-between">
          <h2 className="text-sm font-semibold uppercase tracking-wider text-muted">Open invoices</h2>
          <Input
            className="max-w-xs"
            label="Reference"
            value={reference}
            onChange={(e) => setReference(e.target.value)}
            placeholder="Cheque no., transfer ref…"
          />
        </div>

        {customerKey == null ? (
          <p className="text-sm text-muted">Pick a customer to see its open invoices.</p>
        ) : outstanding.isPending ? (
          <p className="text-sm text-muted">Loading open invoices…</p>
        ) : invoices.length === 0 ? (
          <p className="text-sm text-muted">This customer has nothing outstanding.</p>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-subtle text-left text-xs uppercase tracking-wider text-muted">
                  <th className="py-2 pr-3 font-semibold">Invoice</th>
                  <th className="py-2 pr-3 font-semibold">Date</th>
                  <th className="py-2 pr-3 text-right font-semibold">Total</th>
                  <th className="py-2 pr-3 text-right font-semibold">Outstanding</th>
                  <th className="py-2 pl-3 text-right font-semibold">Allocate</th>
                </tr>
              </thead>
              <tbody>
                {invoices.map((inv) => {
                  const raw = allocations[inv.invoiceId] ?? "";
                  const value = raw ? Number(raw) : 0;
                  const over = Number.isFinite(value) && value > inv.outstanding + 1e-9;
                  return (
                    <tr key={inv.invoiceId} className="border-b border-subtle/60">
                      <td className="py-2 pr-3">
                        <span className="flex items-center gap-2">
                          <span className="font-medium text-text">{inv.number}</span>
                          {inv.origin === "legacy" && <Badge tone="neutral">Legacy</Badge>}
                        </span>
                      </td>
                      <td className="py-2 pr-3 whitespace-nowrap text-muted">{formatReportDate(inv.date)}</td>
                      <td className="py-2 pr-3 text-right tabular text-muted">{formatMoney(inv.total)}</td>
                      <td className="py-2 pr-3 text-right tabular text-text">{formatMoney(inv.outstanding)}</td>
                      <td className="py-2 pl-3">
                        <div className="flex items-center justify-end gap-2">
                          <input
                            inputMode="decimal"
                            value={raw}
                            onChange={(e) => setAllocation(inv.invoiceId, e.target.value)}
                            placeholder="0"
                            aria-label={`Allocate to ${inv.number}`}
                            className={`w-28 rounded-md border bg-surface px-2 py-1 text-right tabular outline-none focus:ring-2 focus:ring-accent/40 ${
                              over ? "border-danger" : "border-subtle"
                            }`}
                          />
                          <button
                            type="button"
                            onClick={() => setAllocation(inv.invoiceId, String(inv.outstanding))}
                            className="text-xs text-accent hover:underline"
                          >
                            Full
                          </button>
                        </div>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </Card>

      <div className="flex items-center justify-end gap-4">
        <div className="text-right">
          <p className="text-xs uppercase tracking-wider text-muted">Receipt total</p>
          <p className="tabular text-2xl font-bold text-text">{formatMoney(total)}</p>
          {overAllocated && <p className="text-xs text-danger">An allocation is over its invoice&apos;s outstanding.</p>}
        </div>
        <Button onClick={submit} pending={submitting} disabled={!canSubmit}>
          Record receipt
        </Button>
      </div>
    </FadeIn>
  );
}
