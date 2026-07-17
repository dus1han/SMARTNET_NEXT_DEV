"use client";

/**
 * Record an expense — Phase 7, slice 3.
 *
 * A flat log entry against a shared category. No ledger, no balance. Dual-writes the legacy expense_tr row.
 */

import { useState } from "react";
import { useRouter } from "next/navigation";
import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { ArrowLeft } from "lucide-react";
import { ApiError } from "@/lib/api";
import { createExpense, getExpenseCategories } from "@/lib/expenses";
import { listCompanies } from "@/lib/customers";
import { today } from "@/lib/period";
import { PageHeader } from "@/components/shell/app-shell";
import { formatMoney } from "@/components/reports";
import { Button, Card, ErrorBanner, FadeIn, Input, Select, toast } from "@/components/ui";

export default function NewExpensePage() {
  const router = useRouter();
  const companies = useQuery({ queryKey: ["companies"], queryFn: listCompanies });
  const categories = useQuery({ queryKey: ["expense-categories"], queryFn: getExpenseCategories });

  const [companyId, setCompanyId] = useState("");
  const [categoryId, setCategoryId] = useState("");
  const [date, setDate] = useState(today);
  const [description, setDescription] = useState("");
  const [amount, setAmount] = useState("");
  const [method, setMethod] = useState("Cash");
  const [reference, setReference] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  const amountValue = amount !== "" ? Number(amount) : 0;
  const canSubmit = companyId !== "" && categoryId !== "" && description.trim() !== "" && amountValue > 0 && !submitting;

  async function submit() {
    setSubmitting(true);
    setError(null);
    try {
      const created = await createExpense({
        companyId: Number(companyId),
        categoryId: Number(categoryId),
        date,
        description: description.trim(),
        amount: amountValue,
        method: method || null,
        reference: reference || null,
      });
      toast.success(`Expense recorded — ${formatMoney(created.amount)}.`);
      router.push("/expenses");
    } catch (e) {
      setError(e as ApiError);
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <FadeIn className="space-y-6">
      <Link href="/expenses" className="inline-flex items-center gap-1.5 text-sm text-muted transition-colors hover:text-text">
        <ArrowLeft className="size-4" aria-hidden />
        All expenses
      </Link>

      <PageHeader title="Record an expense" description="A flat log entry against a shared category. No ledger, no balance." />

      {error && <ErrorBanner message={error.message} correlationId={error.correlationId} />}

      <Card className="grid gap-4 p-5 sm:grid-cols-2 lg:grid-cols-3">
        <Select label="Company" value={companyId} onChange={(e) => setCompanyId(e.target.value)}>
          <option value="">Select…</option>
          {companies.data?.map((c) => (
            <option key={c.id} value={c.id}>{c.name}</option>
          ))}
        </Select>

        <Select label="Category" value={categoryId} onChange={(e) => setCategoryId(e.target.value)}>
          <option value="">Select…</option>
          {categories.data?.map((c) => (
            <option key={c.id} value={c.id}>{c.name}</option>
          ))}
        </Select>

        <Input label="Date" type="date" value={date} onChange={(e) => setDate(e.target.value)} />

        <Input label="Description" value={description} onChange={(e) => setDescription(e.target.value)} className="sm:col-span-2 lg:col-span-1" />

        <Select label="Method" value={method} onChange={(e) => setMethod(e.target.value)}>
          <option value="Cash">Cash</option>
          <option value="Bank">Bank</option>
          <option value="Cheque">Cheque</option>
          <option value="Online">Online</option>
        </Select>

        <Input label="Reference" value={reference} onChange={(e) => setReference(e.target.value)} />
        <Input label="Amount" inputMode="decimal" value={amount} onChange={(e) => setAmount(e.target.value)} placeholder="0" />
      </Card>

      <div className="flex justify-end">
        <Button onClick={submit} pending={submitting} disabled={!canSubmit}>
          Record expense
        </Button>
      </div>
    </FadeIn>
  );
}
