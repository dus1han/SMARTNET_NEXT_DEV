"use client";

/**
 * Record an expense — Phase 7, slice 3.
 *
 * A flat log entry against a shared category. No ledger, no balance. Dual-writes the legacy expense_tr row.
 */

import { useMemo, useState } from "react";
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
  const [invoiceNo, setInvoiceNo] = useState("");
  const [description, setDescription] = useState("");
  const [net, setNet] = useState("");
  const [vat, setVat] = useState("");
  const [amount, setAmount] = useState("");
  const [method, setMethod] = useState("Cash");
  const [reference, setReference] = useState("");
  const [chequePayee, setChequePayee] = useState("");
  const [chequeBank, setChequeBank] = useState("");
  const [chequeNumber, setChequeNumber] = useState("");
  const [chequeDueDate, setChequeDueDate] = useState(today);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  // Net + VAT% suggest the total, which the user can still override (rounding etc.). Total is what's stored.
  const suggested = useMemo(() => {
    const n = Number(net);
    const v = Number(vat);
    if (!Number.isFinite(n) || !Number.isFinite(v)) return null;
    return Math.round(n * (1 + v / 100) * 100) / 100;
  }, [net, vat]);

  const netValue = net !== "" ? Number(net) : 0;
  const amountValue = amount !== "" ? Number(amount) : suggested ?? 0;
  const byCheque = method.toUpperCase() === "CHEQUE";
  const canSubmit =
    companyId !== "" && categoryId !== "" && description.trim() !== "" && amountValue > 0 &&
    (!byCheque || chequePayee.trim() !== "") && !submitting;

  async function submit() {
    setSubmitting(true);
    setError(null);
    try {
      const created = await createExpense({
        companyId: Number(companyId),
        categoryId: Number(categoryId),
        date,
        invoiceNo: invoiceNo.trim() || null,
        description: description.trim(),
        netAmount: net !== "" ? netValue : amountValue,
        taxRatePercentage: vat !== "" ? Number(vat) : 0,
        amount: amountValue,
        method: method || null,
        reference: reference || null,
        chequePayee: byCheque ? chequePayee.trim() : null,
        chequeBank: byCheque ? chequeBank || null : null,
        chequeNumber: byCheque ? chequeNumber || null : null,
        chequeDate: byCheque ? date : null,
        chequeDueDate: byCheque ? chequeDueDate || null : null,
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

        <Input label="Invoice no." value={invoiceNo} onChange={(e) => setInvoiceNo(e.target.value)} placeholder="Bill / invoice number" />

        <Input label="Description" value={description} onChange={(e) => setDescription(e.target.value)} className="sm:col-span-2 lg:col-span-1" />

        <Select label="Method" value={method} onChange={(e) => setMethod(e.target.value)}>
          <option value="Cash">Cash</option>
          <option value="Bank">Bank</option>
          <option value="Cheque">Cheque</option>
          <option value="Online">Online</option>
        </Select>

        <Input label="Reference" value={reference} onChange={(e) => setReference(e.target.value)} />

        <Input label="Net (before VAT)" inputMode="decimal" value={net} onChange={(e) => setNet(e.target.value)} placeholder="0" />
        <Input label="VAT %" inputMode="decimal" value={vat} onChange={(e) => setVat(e.target.value)} placeholder="0" />
        <Input
          label="Total"
          inputMode="decimal"
          value={amount}
          onChange={(e) => setAmount(e.target.value)}
          placeholder={suggested != null ? String(suggested) : "0"}
          hint={suggested != null && amount === "" ? `Net + VAT = ${formatMoney(suggested)} (edit if it differs).` : undefined}
        />

        {byCheque && (
          <>
            <Input label="Cheque payee" required value={chequePayee} onChange={(e) => setChequePayee(e.target.value)} />
            <Input label="Bank" value={chequeBank} onChange={(e) => setChequeBank(e.target.value)} />
            <Input label="Cheque no." value={chequeNumber} onChange={(e) => setChequeNumber(e.target.value)} />
            <Input label="Cheque due date" type="date" value={chequeDueDate} onChange={(e) => setChequeDueDate(e.target.value)} />
            <p className="text-xs text-muted sm:col-span-2 lg:col-span-3">A cheque for this expense will appear in the cheque register, ready to print.</p>
          </>
        )}
      </Card>

      <div className="flex justify-end">
        <Button onClick={submit} pending={submitting} disabled={!canSubmit}>
          Record expense
        </Button>
      </div>
    </FadeIn>
  );
}
