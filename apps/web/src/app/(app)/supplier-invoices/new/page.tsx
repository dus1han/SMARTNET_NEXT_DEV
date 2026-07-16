"use client";

/**
 * Record a supplier invoice — Phase 6 slice 2.
 *
 * A header-only accounts-payable record: the supplier's own reference and the figures they billed (no
 * line items, no tax engine — the supplier's numbers). Saving posts the payable to the payables ledger.
 */

import { useMemo, useState } from "react";
import { useRouter } from "next/navigation";
import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { ArrowLeft } from "lucide-react";
import { ApiError } from "@/lib/api";
import { createSupplierInvoice } from "@/lib/supplier-invoices";
import { listCompanies } from "@/lib/customers";
import { listSuppliers } from "@/lib/suppliers";
import { today } from "@/lib/period";
import { PageHeader } from "@/components/shell/app-shell";
import { formatMoney } from "@/components/reports";
import { Button, Card, ErrorBanner, FadeIn, Input, Select, toast } from "@/components/ui";
import { SupplierCombobox } from "@/components/documents/supplier-combobox";

export default function NewSupplierInvoicePage() {
  const router = useRouter();
  const companies = useQuery({ queryKey: ["companies"], queryFn: listCompanies });
  const suppliers = useQuery({ queryKey: ["suppliers"], queryFn: listSuppliers });

  const [companyId, setCompanyId] = useState("");
  const [supplierId, setSupplierId] = useState("");
  const [reference, setReference] = useState("");
  const [date, setDate] = useState(today);
  const [net, setNet] = useState("");
  const [vat, setVat] = useState("");
  const [amount, setAmount] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  // A convenience: net + VAT% suggests the gross. The user can still type a different amount (the
  // supplier's figure is the authority — rounding and all), so it is a hint, not a lock.
  const suggested = useMemo(() => {
    const n = Number(net);
    const v = Number(vat);
    if (!Number.isFinite(n) || !Number.isFinite(v)) return null;
    return Math.round(n * (1 + v / 100) * 100) / 100;
  }, [net, vat]);

  const amountValue = amount !== "" ? Number(amount) : suggested ?? 0;
  const canSubmit = companyId !== "" && supplierId !== "" && amountValue > 0;

  async function submit() {
    setSubmitting(true);
    setError(null);
    try {
      const created = await createSupplierInvoice({
        companyId: Number(companyId),
        supplierId: Number(supplierId),
        supplierReference: reference || null,
        date,
        netTotal: net !== "" ? Number(net) : 0,
        taxRatePercentage: vat !== "" ? Number(vat) : 0,
        amount: amountValue,
      });
      toast.success(`Supplier invoice recorded — ${formatMoney(created.amount)} payable.`);
      router.push(`/supplier-invoices/${created.id}`);
    } catch (e) {
      setError(e as ApiError);
    } finally {
      setSubmitting(false);
    }
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

      <PageHeader title="New supplier invoice" description="What we owe a supplier — the payable is posted to the ledger, and payments settle against it." />

      {error && <ErrorBanner message={error.message} correlationId={error.correlationId} />}

      <Card className="grid gap-4 p-5 sm:grid-cols-2 lg:grid-cols-3">
        <Select label="Company" value={companyId} onChange={(e) => setCompanyId(e.target.value)}>
          <option value="">Select…</option>
          {companies.data?.map((c) => (
            <option key={c.id} value={c.id}>{c.name}</option>
          ))}
        </Select>

        <SupplierCombobox suppliers={suppliers.data ?? []} value={supplierId} onChange={setSupplierId} />

        <Input
          label="Supplier's invoice no."
          value={reference}
          onChange={(e) => setReference(e.target.value)}
          hint="The number the supplier put on it — theirs, not ours."
        />
        <Input label="Date" type="date" value={date} onChange={(e) => setDate(e.target.value)} />
      </Card>

      <div className="grid gap-4 sm:grid-cols-2">
        <div />
        <Card className="space-y-3 p-5">
          <Input label="Net (before VAT)" inputMode="decimal" value={net} onChange={(e) => setNet(e.target.value)} placeholder="0" />
          <Input label="VAT %" inputMode="decimal" value={vat} onChange={(e) => setVat(e.target.value)} placeholder="0" />
          <Input
            label="Amount (total)"
            inputMode="decimal"
            value={amount}
            onChange={(e) => setAmount(e.target.value)}
            placeholder={suggested != null ? String(suggested) : "0"}
            hint={suggested != null && amount === "" ? `Net + VAT = ${formatMoney(suggested)} (edit if the supplier's total differs).` : undefined}
          />

          <Button className="mt-1 w-full" onClick={submit} pending={submitting} disabled={!canSubmit}>
            Record supplier invoice
          </Button>
        </Card>
      </div>
    </FadeIn>
  );
}
