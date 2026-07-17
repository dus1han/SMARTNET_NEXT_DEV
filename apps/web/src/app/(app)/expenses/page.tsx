"use client";

/**
 * The expenses list (Phase 7, slice 3) — money spent.
 *
 * This app's own expenses and the legacy ones adopted. A flat log — no ledger, no balance. Categories are the
 * shared exp_cat_m, managed from here.
 */

import { useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useRouter } from "next/navigation";
import { Plus, Tags, Trash2 } from "lucide-react";
import { ApiError } from "@/lib/api";
import {
  addExpenseCategory,
  getExpenseCategories,
  getExpenses,
  renameExpenseCategory,
  voidExpense,
  type ExpenseSummary,
} from "@/lib/expenses";
import { PageHeader } from "@/components/shell/app-shell";
import { DataTable, type ColumnDef } from "@/components/data-table";
import { formatMoney, formatReportDate } from "@/components/reports";
import { Badge, Button, Card, Dialog, ErrorBanner, FadeIn, Input, toast } from "@/components/ui";

export default function ExpensesPage() {
  const router = useRouter();
  const expenses = useQuery({ queryKey: ["expenses"], queryFn: getExpenses });
  const error = expenses.error as ApiError | null;

  const [voiding, setVoiding] = useState<ExpenseSummary | null>(null);
  const [viewing, setViewing] = useState<ExpenseSummary | null>(null);
  const [managingCategories, setManagingCategories] = useState(false);

  const columns: ColumnDef<ExpenseSummary, unknown>[] = [
    {
      id: "date",
      accessorFn: (row) => row.date,
      header: "Date",
      cell: ({ row }) => <span className="whitespace-nowrap text-muted">{formatReportDate(row.original.date)}</span>,
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
      id: "actions",
      header: "",
      enableSorting: false,
      cell: ({ row }) => (
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
      ),
    },
  ];

  return (
    <FadeIn className="space-y-6">
      <PageHeader
        title="Expenses"
        description="Money spent — this app's own and the legacy ones. A flat log against a shared set of categories."
      />

      {error && <ErrorBanner message={error.message} correlationId={error.correlationId} />}

      <DataTable
        columns={columns}
        rows={expenses.data}
        loading={expenses.isPending}
        searchable={(row) => `${row.description} ${row.category ?? ""} ${row.reference ?? ""} ${row.method ?? ""}`}
        searchPlaceholder="Search by description, category, reference…"
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
          onVoid={() => {
            const e = viewing;
            setViewing(null);
            setVoiding(e);
          }}
        />
      )}
      {voiding && <VoidExpenseDialog expense={voiding} onClose={() => setVoiding(null)} />}
      <CategoriesDialog open={managingCategories} onOpenChange={setManagingCategories} />
    </FadeIn>
  );
}

function ExpenseDetailDialog({ expense, onClose, onVoid }: { expense: ExpenseSummary; onClose: () => void; onVoid: () => void }) {
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
          <Button onClick={onClose}>Close</Button>
        </>
      }
    >
      <div className="grid gap-3 sm:grid-cols-2">
        <Detail label="Company" value={expense.companyName ?? "—"} />
        <Detail label="Category" value={expense.category ?? "—"} />
        <Detail label="Date" value={formatReportDate(expense.date)} />
        <Detail label="Method" value={expense.method || "—"} />
        <Detail label="Net (before VAT)" value={formatMoney(expense.netAmount)} />
        <Detail label="VAT" value={formatMoney(expense.taxAmount)} />
        <Detail label="Total" value={formatMoney(expense.amount)} />
        <Detail label="Reference" value={expense.reference || "—"} />
        <div className="sm:col-span-2">
          <Detail label="Description" value={expense.description || "—"} />
        </div>
        {expense.origin === "legacy" && (
          <div className="sm:col-span-2">
            <Badge tone="neutral">Legacy</Badge>
          </div>
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
      description="Soft-deleted and audited — its history is kept (the legacy delete removed the row)."
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
