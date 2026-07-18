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
import { today } from "@/lib/period";
import { PageHeader } from "@/components/shell/app-shell";
import { formatMoney } from "@/components/reports";
import { Button, Card, ErrorBanner, FadeIn, Input, Select, toast } from "@/components/ui";

export default function NewChequePage() {
  const router = useRouter();
  const companies = useQuery({ queryKey: ["companies"], queryFn: listCompanies });

  const [companyId, setCompanyId] = useState("");
  const [payTo, setPayTo] = useState("");
  const [bank, setBank] = useState("");
  const [chequeNumber, setChequeNumber] = useState("");
  const [amount, setAmount] = useState("");
  const [chequeDate, setChequeDate] = useState(today);
  const [dueDate, setDueDate] = useState(today);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  const amountValue = amount !== "" ? Number(amount) : 0;
  const canSubmit = companyId !== "" && payTo.trim() !== "" && amountValue > 0 && !submitting;

  async function submit() {
    setSubmitting(true);
    setError(null);
    try {
      // A standalone cheque is always Manual — a cheque to a supplier is raised from a supplier payment.
      const created = await createCheque({
        companyId: Number(companyId),
        entryType: "Manual",
        payTo: payTo.trim(),
        supplierId: null,
        bank: bank || null,
        chequeNumber: chequeNumber || null,
        amount: amountValue,
        chequeDate: chequeDate || null,
        dueDate: dueDate || null,
      });
      toast.success(`Cheque recorded — ${formatMoney(created.amount)} to ${payTo.trim()}.`);
      // Straight to the print preview, as a new job card, quotation or invoice does — a cheque is
      // written in order to be printed, so the preview is the next step, not a second decision.
      router.push(`/cheques/${created.id}?print=1`);
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

      <PageHeader title="Record a cheque" description="A standalone cheque to any payee. A cheque to a supplier is raised from a supplier payment; an expense cheque from an expense — those appear here automatically." />

      {error && <ErrorBanner message={error.message} correlationId={error.correlationId} />}

      <Card className="grid gap-4 p-5 sm:grid-cols-2 lg:grid-cols-3">
        <Select label="Company" value={companyId} onChange={(e) => setCompanyId(e.target.value)}>
          <option value="">Select…</option>
          {companies.data?.map((c) => (
            <option key={c.id} value={c.id}>{c.name}</option>
          ))}
        </Select>

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
