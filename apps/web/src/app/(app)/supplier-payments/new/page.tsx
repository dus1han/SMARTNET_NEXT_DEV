"use client";

/**
 * Record a supplier payment — Phase 7.
 *
 * The payables mirror of the customer-receipt flow. Pick a supplier, and its outstanding invoices (new and
 * legacy alike, derived from the payables ledger) appear to allocate against. Each allocation posts a Payment
 * entry to the ledger and dual-writes the legacy shadow. An idempotency key (minted once per form) makes a
 * double-submit return the first payment rather than pay twice.
 */

import { useMemo, useState } from "react";
import { useRouter } from "next/navigation";
import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { ArrowLeft } from "lucide-react";
import { ApiError } from "@/lib/api";
import { createSupplierPayment, getOutstandingSupplierInvoices } from "@/lib/supplier-payments";
import { listCompanies } from "@/lib/customers";
import { listSuppliers } from "@/lib/suppliers";
import { today } from "@/lib/period";
import { PageHeader } from "@/components/shell/app-shell";
import { SupplierCombobox } from "@/components/documents/supplier-combobox";
import { formatMoney, formatReportDate } from "@/components/reports";
import { Badge, Button, Card, ErrorBanner, FadeIn, Input, Select, toast } from "@/components/ui";

export default function NewSupplierPaymentPage() {
  const router = useRouter();
  const companies = useQuery({ queryKey: ["companies"], queryFn: listCompanies });
  const suppliers = useQuery({ queryKey: ["suppliers"], queryFn: listSuppliers });

  const [companyId, setCompanyId] = useState("");
  const [supplierId, setSupplierId] = useState("");
  const [date, setDate] = useState(today);
  const [method, setMethod] = useState("BANK");
  const [reference, setReference] = useState("");
  const [allocations, setAllocations] = useState<Record<number, string>>({});
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  // Minted once per form instance — a resubmit of the same payment carries the same key and is deduped.
  const [idempotencyKey] = useState(() => crypto.randomUUID());

  const supplierKey = supplierId === "" ? null : Number(supplierId);
  const outstanding = useQuery({
    queryKey: ["outstanding-supplier-invoices", supplierKey],
    queryFn: () => getOutstandingSupplierInvoices(supplierKey!),
    enabled: supplierKey != null,
  });

  const invoices = useMemo(() => outstanding.data ?? [], [outstanding.data]);

  const { total, overAllocated, positiveCount } = useMemo(() => {
    let sum = 0;
    let over = false;
    let count = 0;
    for (const inv of invoices) {
      const raw = allocations[inv.supplierInvoiceId];
      const value = raw ? Number(raw) : 0;
      if (Number.isFinite(value) && value > 0) {
        sum += value;
        count += 1;
        if (value > inv.outstanding + 1e-9) over = true;
      }
    }
    return { total: Math.round(sum * 100) / 100, overAllocated: over, positiveCount: count };
  }, [invoices, allocations]);

  const canSubmit = companyId !== "" && supplierKey != null && positiveCount > 0 && !overAllocated && !submitting;

  function setAllocation(invoiceId: number, value: string) {
    setAllocations((prev) => ({ ...prev, [invoiceId]: value }));
  }

  function resetSupplier(id: string) {
    setSupplierId(id);
    setAllocations({});
  }

  async function submit() {
    setSubmitting(true);
    setError(null);
    try {
      const lines = invoices
        .map((inv) => ({
          supplierInvoiceId: inv.supplierInvoiceId,
          amount: allocations[inv.supplierInvoiceId] ? Number(allocations[inv.supplierInvoiceId]) : 0,
        }))
        .filter((a) => a.amount > 0);

      const created = await createSupplierPayment({
        companyId: Number(companyId),
        supplierId: supplierKey!,
        date,
        method: method || null,
        reference: reference || null,
        idempotencyKey,
        allocations: lines,
      });
      toast.success(
        created.alreadyExisted
          ? "That payment was already recorded."
          : `Payment recorded — ${formatMoney(created.amount)} across ${lines.length} invoice${lines.length === 1 ? "" : "s"}.`,
      );
      router.push(`/supplier-payments/${created.id}`);
    } catch (e) {
      setError(e as ApiError);
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <FadeIn className="space-y-6">
      <Link
        href="/supplier-payments"
        className="inline-flex items-center gap-1.5 text-sm text-muted transition-colors hover:text-text"
      >
        <ArrowLeft className="size-4" aria-hidden />
        All supplier payments
      </Link>

      <PageHeader
        title="Record a supplier payment"
        description="Money paid to a supplier, allocated across its open invoices. Each allocation settles against the payables ledger."
      />

      {error && <ErrorBanner message={error.message} correlationId={error.correlationId} />}

      <Card className="grid gap-4 p-5 sm:grid-cols-2 lg:grid-cols-4">
        <Select label="Company" value={companyId} onChange={(e) => setCompanyId(e.target.value)}>
          <option value="">Select…</option>
          {companies.data?.map((c) => (
            <option key={c.id} value={c.id}>{c.name}</option>
          ))}
        </Select>

        <SupplierCombobox suppliers={suppliers.data ?? []} value={supplierId} onChange={resetSupplier} />

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

        {supplierKey == null ? (
          <p className="text-sm text-muted">Pick a supplier to see its open invoices.</p>
        ) : outstanding.isPending ? (
          <p className="text-sm text-muted">Loading open invoices…</p>
        ) : invoices.length === 0 ? (
          <p className="text-sm text-muted">This supplier has nothing outstanding.</p>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-subtle text-left text-xs uppercase tracking-wider text-muted">
                  <th className="py-2 pr-3 font-semibold">Invoice</th>
                  <th className="py-2 pr-3 font-semibold">Date</th>
                  <th className="py-2 pr-3 text-right font-semibold">Amount</th>
                  <th className="py-2 pr-3 text-right font-semibold">Outstanding</th>
                  <th className="py-2 pl-3 text-right font-semibold">Allocate</th>
                </tr>
              </thead>
              <tbody>
                {invoices.map((inv) => {
                  const raw = allocations[inv.supplierInvoiceId] ?? "";
                  const value = raw ? Number(raw) : 0;
                  const over = Number.isFinite(value) && value > inv.outstanding + 1e-9;
                  return (
                    <tr key={inv.supplierInvoiceId} className="border-b border-subtle/60">
                      <td className="py-2 pr-3">
                        <span className="flex items-center gap-2">
                          <span className="font-medium text-text">{inv.reference}</span>
                          {inv.origin === "legacy" && <Badge tone="neutral">Legacy</Badge>}
                        </span>
                      </td>
                      <td className="py-2 pr-3 whitespace-nowrap text-muted">{formatReportDate(inv.date)}</td>
                      <td className="py-2 pr-3 text-right tabular text-muted">{formatMoney(inv.amount)}</td>
                      <td className="py-2 pr-3 text-right tabular text-text">{formatMoney(inv.outstanding)}</td>
                      <td className="py-2 pl-3">
                        <div className="flex items-center justify-end gap-2">
                          <input
                            inputMode="decimal"
                            value={raw}
                            onChange={(e) => setAllocation(inv.supplierInvoiceId, e.target.value)}
                            placeholder="0"
                            aria-label={`Allocate to ${inv.reference}`}
                            className={`w-28 rounded-md border bg-surface px-2 py-1 text-right tabular outline-none focus:ring-2 focus:ring-accent/40 ${
                              over ? "border-danger" : "border-subtle"
                            }`}
                          />
                          <button
                            type="button"
                            onClick={() => setAllocation(inv.supplierInvoiceId, String(inv.outstanding))}
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
          <p className="text-xs uppercase tracking-wider text-muted">Payment total</p>
          <p className="tabular text-2xl font-bold text-text">{formatMoney(total)}</p>
          {overAllocated && <p className="text-xs text-danger">An allocation is over its invoice&apos;s outstanding.</p>}
        </div>
        <Button onClick={submit} pending={submitting} disabled={!canSubmit}>
          Record payment
        </Button>
      </div>
    </FadeIn>
  );
}
