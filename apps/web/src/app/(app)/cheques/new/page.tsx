"use client";

/**
 * Record a cheque — Phase 7, slice 2.
 *
 * A standalone written record. Manual entry is a free-text payee; Supplier entry picks a supplier (and fills
 * the payee). No ledger, no balance. Printing is Phase 8.
 */

import { useState } from "react";
import { useRouter } from "next/navigation";
import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { ArrowLeft } from "lucide-react";
import { ApiError } from "@/lib/api";
import { createCheque } from "@/lib/cheques";
import { listCompanies } from "@/lib/customers";
import { listSuppliers } from "@/lib/suppliers";
import { today } from "@/lib/period";
import { PageHeader } from "@/components/shell/app-shell";
import { SupplierCombobox } from "@/components/documents/supplier-combobox";
import { formatMoney } from "@/components/reports";
import { Button, Card, ErrorBanner, FadeIn, Input, Select, toast } from "@/components/ui";

export default function NewChequePage() {
  const router = useRouter();
  const companies = useQuery({ queryKey: ["companies"], queryFn: listCompanies });
  const suppliers = useQuery({ queryKey: ["suppliers"], queryFn: listSuppliers });

  const [companyId, setCompanyId] = useState("");
  const [entryType, setEntryType] = useState("Manual");
  const [supplierId, setSupplierId] = useState("");
  const [payTo, setPayTo] = useState("");
  const [bank, setBank] = useState("");
  const [chequeNumber, setChequeNumber] = useState("");
  const [amount, setAmount] = useState("");
  const [chequeDate, setChequeDate] = useState(today);
  const [dueDate, setDueDate] = useState(today);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  const amountValue = amount !== "" ? Number(amount) : 0;
  const isSupplier = entryType === "Supplier";
  const canSubmit =
    companyId !== "" && payTo.trim() !== "" && amountValue > 0 && (!isSupplier || supplierId !== "") && !submitting;

  function pickSupplier(id: string) {
    setSupplierId(id);
    const supplier = suppliers.data?.find((s) => String(s.id) === id);
    if (supplier) setPayTo(supplier.name ?? "");
  }

  async function submit() {
    setSubmitting(true);
    setError(null);
    try {
      const created = await createCheque({
        companyId: Number(companyId),
        entryType,
        payTo: payTo.trim(),
        supplierId: isSupplier && supplierId !== "" ? Number(supplierId) : null,
        bank: bank || null,
        chequeNumber: chequeNumber || null,
        amount: amountValue,
        chequeDate: chequeDate || null,
        dueDate: dueDate || null,
      });
      toast.success(`Cheque recorded — ${formatMoney(created.amount)} to ${payTo.trim()}.`);
      router.push(`/cheques/${created.id}`);
    } catch (e) {
      setError(e as ApiError);
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <FadeIn className="space-y-6">
      <Link href="/cheques" className="inline-flex items-center gap-1.5 text-sm text-muted transition-colors hover:text-text">
        <ArrowLeft className="size-4" aria-hidden />
        All cheques
      </Link>

      <PageHeader title="Record a cheque" description="A standalone written record. No ledger, no balance — printing arrives in Phase 8." />

      {error && <ErrorBanner message={error.message} correlationId={error.correlationId} />}

      <Card className="grid gap-4 p-5 sm:grid-cols-2 lg:grid-cols-3">
        <Select label="Company" value={companyId} onChange={(e) => setCompanyId(e.target.value)}>
          <option value="">Select…</option>
          {companies.data?.map((c) => (
            <option key={c.id} value={c.id}>{c.name}</option>
          ))}
        </Select>

        <Select label="Entry" value={entryType} onChange={(e) => { setEntryType(e.target.value); setSupplierId(""); }}>
          <option value="Manual">Manual</option>
          <option value="Supplier">Supplier</option>
        </Select>

        {isSupplier ? (
          <SupplierCombobox suppliers={suppliers.data ?? []} value={supplierId} onChange={pickSupplier} />
        ) : (
          <div />
        )}

        <Input label="Pay to" value={payTo} onChange={(e) => setPayTo(e.target.value)} placeholder="Payee" />
        <Input label="Bank" value={bank} onChange={(e) => setBank(e.target.value)} />
        <Input label="Cheque no." value={chequeNumber} onChange={(e) => setChequeNumber(e.target.value)} />
        <Input label="Cheque date" type="date" value={chequeDate} onChange={(e) => setChequeDate(e.target.value)} />
        <Input label="Due date" type="date" value={dueDate} onChange={(e) => setDueDate(e.target.value)} />
        <Input label="Amount" inputMode="decimal" value={amount} onChange={(e) => setAmount(e.target.value)} placeholder="0" />
      </Card>

      <div className="flex justify-end">
        <Button onClick={submit} pending={submitting} disabled={!canSubmit}>
          Record cheque
        </Button>
      </div>
    </FadeIn>
  );
}
