"use client";

/**
 * The expenses list (Phase 7, slice 3) — money owed and money spent.
 *
 * This app's own expenses and the legacy ones adopted. An expense is recorded when it is incurred and
 * settled afterwards, in one payment or several, so what it still owes is derived (amount − payments)
 * rather than kept in a flag. Categories are the shared exp_cat_m, managed from here.
 */

import { useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useRouter } from "next/navigation";
import { Plus, Tags, Trash2, Wallet } from "lucide-react";
import { ApiError } from "@/lib/api";
import {
  addExpenseCategory,
  getExpenseCategories,
  getExpensePayments,
  getExpenses,
  recordExpensePayment,
  renameExpenseCategory,
  voidExpense,
  voidExpensePayment,
  type ExpensePaymentSummary,
  type ExpenseSummary,
} from "@/lib/expenses";
import { today } from "@/lib/period";
import { PageHeader } from "@/components/shell/app-shell";
import { DataTable, type ColumnDef } from "@/components/data-table";
import { formatMoney, formatReportDate } from "@/components/reports";
import { Badge, Button, Card, Dialog, ErrorBanner, FadeIn, Input, Select, toast } from "@/components/ui";

/** Settled, part settled or outstanding — read off the derived figures, never a stored flag. */
function status(expense: ExpenseSummary) {
  if (expense.outstanding <= 0) return { label: "Settled", tone: "success" as const };
  if (expense.paidAmount > 0) return { label: "Part settled", tone: "warning" as const };
  return { label: "Outstanding", tone: "warning" as const };
}

export default function ExpensesPage() {
  const router = useRouter();
  const expenses = useQuery({ queryKey: ["expenses"], queryFn: getExpenses });
  const error = expenses.error as ApiError | null;

  const [voiding, setVoiding] = useState<ExpenseSummary | null>(null);
  const [viewing, setViewing] = useState<ExpenseSummary | null>(null);
  const [settling, setSettling] = useState<ExpenseSummary | null>(null);
  const [voidingPayment, setVoidingPayment] = useState<{ expense: ExpenseSummary; payment: ExpensePaymentSummary } | null>(null);
  const [managingCategories, setManagingCategories] = useState(false);

  const columns: ColumnDef<ExpenseSummary, unknown>[] = [
    {
      id: "date",
      accessorFn: (row) => row.date,
      header: "Date",
      cell: ({ row }) => <span className="whitespace-nowrap text-muted">{formatReportDate(row.original.date)}</span>,
    },
    {
      id: "invoiceNo",
      accessorFn: (row) => row.invoiceNo ?? "",
      header: "Invoice no.",
      cell: ({ row }) => <span className="tabular text-muted">{row.original.invoiceNo || "—"}</span>,
    },
    {
      id: "category",
      accessorFn: (row) => row.category ?? "",
      header: "Category",
      cell: ({ row }) => <span className="text-text">{row.original.category || "—"}</span>,
    },
    {
      id: "description",
      accessorFn: (row) => row.description,
      header: "Description",
      cell: ({ row }) => (
        <span className="flex items-center gap-2">
          <span className="font-medium text-text">{row.original.description || "—"}</span>
          {row.original.origin === "legacy" && <Badge tone="neutral">Legacy</Badge>}
        </span>
      ),
    },
    {
      id: "method",
      accessorFn: (row) => row.method ?? "",
      header: "Method",
      cell: ({ row }) => <span className="text-text">{row.original.method || "—"}</span>,
    },
    {
      id: "amount",
      accessorFn: (row) => row.amount,
      header: "Amount",
      meta: { align: "right" },
      cell: ({ row }) => <span className="tabular font-medium text-text">{formatMoney(row.original.amount)}</span>,
    },
    {
      id: "outstanding",
      accessorFn: (row) => row.outstanding,
      header: "Outstanding",
      meta: { align: "right" },
      cell: ({ row }) => (
        <span className="tabular text-muted">
          {row.original.outstanding > 0 ? formatMoney(row.original.outstanding) : "—"}
        </span>
      ),
    },
    {
      id: "status",
      accessorFn: (row) => status(row).label,
      header: "Status",
      cell: ({ row }) => {
        const s = status(row.original);
        return <Badge tone={s.tone}>{s.label}</Badge>;
      },
    },
    {
      id: "actions",
      header: "",
      enableSorting: false,
      cell: ({ row }) => (
        <div className="flex justify-end gap-1">
          {row.original.outstanding > 0 && (
            <Button
              variant="ghost"
              size="icon"
              aria-label="Settle expense"
              onClick={(e) => {
                e.stopPropagation();
                setSettling(row.original);
              }}
            >
              <Wallet className="text-muted" />
            </Button>
          )}
          <Button
            variant="ghost"
            size="icon"
            aria-label="Void expense"
            onClick={(e) => {
              e.stopPropagation();
              setVoiding(row.original);
            }}
          >
            <Trash2 className="text-muted" />
          </Button>
        </div>
      ),
    },
  ];

  return (
    <FadeIn className="space-y-6">
      <PageHeader
        title="Expenses"
        description="Money spent — this app's own and the legacy ones. Recorded when incurred, settled in full or in instalments."
      />

      {error && <ErrorBanner message={error.message} correlationId={error.correlationId} />}

      <DataTable
        columns={columns}
        rows={expenses.data}
        loading={expenses.isPending}
        searchable={(row) => `${row.description} ${row.category ?? ""} ${row.reference ?? ""} ${row.method ?? ""} ${row.vatNumber ?? ""}`}
        searchPlaceholder="Search by description, category, reference, VAT no.…"
        defaultSort={{ id: "date", desc: true }}
        onRowClick={(row) => setViewing(row)}
        actions={
          <>
            <Button variant="secondary" size="sm" onClick={() => setManagingCategories(true)}>
              <Tags />
              Categories
            </Button>
            <Button size="sm" onClick={() => router.push("/expenses/new")}>
              <Plus />
              Record an expense
            </Button>
          </>
        }
        empty={{
          title: "No expenses yet",
          description: "Expenses recorded in the new system — and the legacy ones — appear here.",
        }}
      />

      {viewing && (
        <ExpenseDetailDialog
          expense={viewing}
          onClose={() => setViewing(null)}
          onSettle={() => {
            const e = viewing;
            setViewing(null);
            setSettling(e);
          }}
          onVoid={() => {
            const e = viewing;
            setViewing(null);
            setVoiding(e);
          }}
          onVoidPayment={(payment) => {
            const e = viewing;
            setViewing(null);
            setVoidingPayment({ expense: e, payment });
          }}
        />
      )}
      {settling && <SettleExpenseDialog expense={settling} onClose={() => setSettling(null)} />}
      {voidingPayment && (
        <VoidPaymentDialog
          expense={voidingPayment.expense}
          payment={voidingPayment.payment}
          onClose={() => setVoidingPayment(null)}
        />
      )}
      {voiding && <VoidExpenseDialog expense={voiding} onClose={() => setVoiding(null)} />}
      <CategoriesDialog open={managingCategories} onOpenChange={setManagingCategories} />
    </FadeIn>
  );
}

function ExpenseDetailDialog({ expense, onClose, onSettle, onVoid, onVoidPayment }: {
  expense: ExpenseSummary;
  onClose: () => void;
  onSettle: () => void;
  onVoid: () => void;
  onVoidPayment: (payment: ExpensePaymentSummary) => void;
}) {
  const payments = useQuery({
    queryKey: ["expense-payments", expense.id],
    queryFn: () => getExpensePayments(expense.id),
  });
  const state = status(expense);

  return (
    <Dialog
      open
      onOpenChange={(next) => !next && onClose()}
      title={`Expense · ${formatMoney(expense.amount)}`}
      description={`${expense.description} · ${formatReportDate(expense.date)}`}
      footer={
        <>
          <Button variant="secondary" onClick={onVoid}>
            <Trash2 />
            Void
          </Button>
          {expense.outstanding > 0 && (
            <Button onClick={onSettle}>
              <Wallet />
              Settle
            </Button>
          )}
          <Button variant={expense.outstanding > 0 ? "secondary" : "primary"} onClick={onClose}>
            Close
          </Button>
        </>
      }
    >
      <div className="grid gap-3 sm:grid-cols-2">
        <Detail label="Company" value={expense.companyName ?? "—"} />
        <Detail label="Category" value={expense.category ?? "—"} />
        <Detail label="Date" value={formatReportDate(expense.date)} />
        <Detail label="Invoice no." value={expense.invoiceNo || "—"} />
        <Detail label="Method" value={expense.method || "—"} />
        <Detail label="Net (before VAT)" value={formatMoney(expense.netAmount)} />
        <Detail label="VAT" value={formatMoney(expense.taxAmount)} />
        <Detail label="VAT no." value={expense.vatNumber || "—"} />
        <Detail label="Total" value={formatMoney(expense.amount)} />
        <Detail label="Settled" value={formatMoney(expense.paidAmount)} />
        <Detail label="Outstanding" value={formatMoney(expense.outstanding)} />
        <Detail label="Reference" value={expense.reference || "—"} />
        <div className="sm:col-span-2">
          <Detail label="Description" value={expense.description || "—"} />
        </div>
        <div className="flex items-center gap-2 sm:col-span-2">
          <Badge tone={state.tone}>{state.label}</Badge>
          {expense.origin === "legacy" && <Badge tone="neutral">Legacy</Badge>}
        </div>

        <div className="sm:col-span-2">
          <p className="text-xs font-semibold uppercase tracking-wider text-muted">Payments</p>
          {payments.isPending && <p className="mt-1 text-sm text-muted">Loading…</p>}
          {payments.data?.length === 0 && <p className="mt-1 text-sm text-muted">Nothing paid yet.</p>}
          <div className="mt-1 space-y-1">
            {payments.data?.map((p) => (
              <PaymentRow key={p.id} payment={p} onVoid={() => onVoidPayment(p)} />
            ))}
          </div>
        </div>
      </div>
    </Dialog>
  );
}

/** One settlement, with the void that takes it back off. */
function PaymentRow({ payment, onVoid }: { payment: ExpensePaymentSummary; onVoid: () => void }) {
  return (
    <div className="flex items-center justify-between gap-2 rounded-md bg-surface-sunken px-2.5 py-1.5 text-sm">
      <span className="whitespace-nowrap text-muted">{formatReportDate(payment.date)}</span>
      <span className="min-w-0 flex-1 truncate text-text">
        {payment.method || "—"}
        {payment.reference ? ` · ${payment.reference}` : ""}
        {payment.origin === "migrated" && " · recorded with the expense"}
      </span>
      <span className="tabular font-medium text-text">{formatMoney(payment.amount)}</span>
      <Button variant="ghost" size="icon" aria-label="Void payment" onClick={onVoid}>
        <Trash2 className="text-muted" />
      </Button>
    </div>
  );
}

function VoidPaymentDialog({ expense, payment, onClose }: {
  expense: ExpenseSummary;
  payment: ExpensePaymentSummary;
  onClose: () => void;
}) {
  const queryClient = useQueryClient();
  const [reason, setReason] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  async function submit() {
    setSubmitting(true);
    setError(null);
    try {
      await voidExpensePayment(payment.id, payment.rowVersion, reason);
      void queryClient.invalidateQueries({ queryKey: ["expenses"] });
      void queryClient.invalidateQueries({ queryKey: ["expense-payments", expense.id] });
      toast.success("Payment voided.");
      onClose();
    } catch (e) {
      setError(e as ApiError);
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <Dialog
      open
      onOpenChange={(next) => !next && onClose()}
      title="Void payment"
      description={`${formatMoney(payment.amount)} of "${expense.description}". Soft-deleted and audited — the expense goes back to owing that much.`}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={submitting}>Cancel</Button>
          <Button onClick={submit} pending={submitting} disabled={reason.trim().length < 10}>Void</Button>
        </>
      }
    >
      <div className="space-y-4">
        {error && <ErrorBanner message={error.message} correlationId={error.correlationId} />}
        <Input
          label="Reason"
          value={reason}
          onChange={(e) => setReason(e.target.value)}
          hint="At least 10 characters — recorded on the audit trail."
          placeholder="Why is this payment being voided?"
        />
      </div>
    </Dialog>
  );
}

function SettleExpenseDialog({ expense, onClose }: { expense: ExpenseSummary; onClose: () => void }) {
  const queryClient = useQueryClient();
  const [amount, setAmount] = useState(String(expense.outstanding));
  const [date, setDate] = useState(today);
  const [method, setMethod] = useState("Cash");
  const [reference, setReference] = useState("");
  const [chequePayee, setChequePayee] = useState("");
  const [chequeBank, setChequeBank] = useState("");
  const [chequeNumber, setChequeNumber] = useState("");
  const [chequeDueDate, setChequeDueDate] = useState(today);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  const value = Number(amount);
  const byCheque = method.toUpperCase() === "CHEQUE";
  const valid =
    Number.isFinite(value) && value > 0 && value <= expense.outstanding &&
    (!byCheque || chequePayee.trim() !== "") && !submitting;

  async function submit() {
    setSubmitting(true);
    setError(null);
    try {
      const result = await recordExpensePayment(expense.id, {
        amount: value,
        date,
        method: method || null,
        reference: reference.trim() || null,
        chequePayee: byCheque ? chequePayee.trim() : null,
        chequeBank: byCheque ? chequeBank || null : null,
        chequeNumber: byCheque ? chequeNumber || null : null,
        chequeDate: byCheque ? date : null,
        chequeDueDate: byCheque ? chequeDueDate || null : null,
      });
      void queryClient.invalidateQueries({ queryKey: ["expenses"] });
      void queryClient.invalidateQueries({ queryKey: ["expense-payments", expense.id] });
      toast.success(
        result.outstanding === 0
          ? "Expense settled."
          : `Payment recorded — ${formatMoney(result.outstanding)} still outstanding.`,
      );
      onClose();
    } catch (e) {
      setError(e as ApiError);
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <Dialog
      open
      onOpenChange={(next) => !next && onClose()}
      title="Settle expense"
      description={`${formatMoney(expense.outstanding)} outstanding on "${expense.description}". Pay all of it, or part.`}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={submitting}>Cancel</Button>
          <Button onClick={submit} pending={submitting} disabled={!valid}>Record payment</Button>
        </>
      }
    >
      <div className="space-y-4">
        {error && <ErrorBanner message={error.message} correlationId={error.correlationId} />}
        <Input
          label="Amount"
          inputMode="decimal"
          value={amount}
          onChange={(e) => setAmount(e.target.value)}
          placeholder="0"
          hint={value > expense.outstanding ? `More than the ${formatMoney(expense.outstanding)} outstanding.` : undefined}
        />
        <Input label="Date" type="date" value={date} onChange={(e) => setDate(e.target.value)} />
        <Select label="Method" value={method} onChange={(e) => setMethod(e.target.value)}>
          <option value="Cash">Cash</option>
          <option value="Bank">Bank</option>
          <option value="Cheque">Cheque</option>
          <option value="Online">Online</option>
        </Select>
        <Input label="Reference" value={reference} onChange={(e) => setReference(e.target.value)} />

        {byCheque && (
          <>
            <Input label="Cheque payee" required value={chequePayee} onChange={(e) => setChequePayee(e.target.value)} />
            <Input label="Bank" value={chequeBank} onChange={(e) => setChequeBank(e.target.value)} />
            <Input label="Cheque no." value={chequeNumber} onChange={(e) => setChequeNumber(e.target.value)} />
            <Input label="Cheque due date" type="date" value={chequeDueDate} onChange={(e) => setChequeDueDate(e.target.value)} />
            <p className="text-xs text-muted">A cheque for this payment will appear in the cheque register, ready to print.</p>
          </>
        )}
      </div>
    </Dialog>
  );
}

function Detail({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <p className="text-xs font-semibold uppercase tracking-wider text-muted">{label}</p>
      <p className="mt-0.5 text-sm text-text">{value}</p>
    </div>
  );
}

function VoidExpenseDialog({ expense, onClose }: { expense: ExpenseSummary; onClose: () => void }) {
  const queryClient = useQueryClient();
  const [reason, setReason] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  async function submit() {
    setSubmitting(true);
    setError(null);
    try {
      await voidExpense(expense.id, expense.rowVersion, reason);
      void queryClient.invalidateQueries({ queryKey: ["expenses"] });
      toast.success("Expense voided.");
      onClose();
    } catch (e) {
      setError(e as ApiError);
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <Dialog
      open
      onOpenChange={(next) => !next && onClose()}
      title="Void expense"
      description="Soft-deleted and audited — its history is kept (the legacy delete removed the row). Any payments against it must be voided first."
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={submitting}>Cancel</Button>
          <Button onClick={submit} pending={submitting} disabled={reason.trim().length < 10}>Void</Button>
        </>
      }
    >
      <div className="space-y-4">
        {error && <ErrorBanner message={error.message} correlationId={error.correlationId} />}
        <Input
          label="Reason"
          value={reason}
          onChange={(e) => setReason(e.target.value)}
          hint="At least 10 characters — recorded on the audit trail."
          placeholder={`Why is "${expense.description}" being voided?`}
        />
      </div>
    </Dialog>
  );
}

function CategoriesDialog({ open, onOpenChange }: { open: boolean; onOpenChange: (open: boolean) => void }) {
  const queryClient = useQueryClient();
  const categories = useQuery({ queryKey: ["expense-categories"], queryFn: getExpenseCategories, enabled: open });
  const [newName, setNewName] = useState("");
  const [error, setError] = useState<ApiError | null>(null);

  function refresh() {
    void queryClient.invalidateQueries({ queryKey: ["expense-categories"] });
    void queryClient.invalidateQueries({ queryKey: ["expenses"] });
  }

  async function add() {
    if (newName.trim() === "") return;
    setError(null);
    try {
      await addExpenseCategory({ name: newName.trim() });
      setNewName("");
      refresh();
    } catch (e) {
      setError(e as ApiError);
    }
  }

  async function rename(id: number, name: string) {
    setError(null);
    try {
      await renameExpenseCategory(id, { name });
      refresh();
    } catch (e) {
      setError(e as ApiError);
    }
  }

  return (
    <Dialog
      open={open}
      onOpenChange={onOpenChange}
      title="Expense categories"
      description="Shared across companies. Renaming one updates every expense that uses it."
      footer={<Button variant="secondary" onClick={() => onOpenChange(false)}>Done</Button>}
    >
      <div className="space-y-4">
        {error && <ErrorBanner message={error.message} correlationId={error.correlationId} />}

        <div className="flex items-end gap-2">
          <Input label="New category" value={newName} onChange={(e) => setNewName(e.target.value)} className="flex-1" />
          <Button onClick={add} disabled={newName.trim() === ""}>Add</Button>
        </div>

        <Card className="max-h-72 space-y-1 overflow-y-auto p-2">
          {categories.data?.length === 0 && <p className="p-2 text-sm text-muted">No categories yet.</p>}
          {categories.data?.map((c) => (
            <CategoryRow key={c.id} id={c.id} name={c.name} onRename={rename} />
          ))}
        </Card>
      </div>
    </Dialog>
  );
}

function CategoryRow({ id, name, onRename }: { id: number; name: string; onRename: (id: number, name: string) => void }) {
  const [value, setValue] = useState(name);
  return (
    <div className="flex items-center gap-2">
      <input
        value={value}
        onChange={(e) => setValue(e.target.value)}
        aria-label={`Rename ${name}`}
        className="min-w-0 flex-1 rounded-md border border-subtle bg-surface px-2.5 py-1.5 text-sm text-text focus:border-strong focus:outline-none focus:ring-2 focus:ring-ring/25"
      />
      <Button variant="ghost" size="sm" disabled={value.trim() === "" || value === name} onClick={() => onRename(id, value.trim())}>
        Save
      </Button>
    </div>
  );
}
