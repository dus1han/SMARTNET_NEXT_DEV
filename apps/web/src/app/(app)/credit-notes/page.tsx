"use client";

/**
 * The credit-note list.
 *
 * The credit notes this app has raised (and those adopted from the legacy system), newest first. A credit
 * note reverses part or all of an invoice, so what it shows is which invoice it credits and for how much.
 */

import { useQuery } from "@tanstack/react-query";
import { useRouter } from "next/navigation";
import { Plus } from "lucide-react";
import { ApiError } from "@/lib/api";
import { getCreditNotes, type CreditNoteSummary } from "@/lib/credit-notes";
import { PageHeader } from "@/components/shell/app-shell";
import { DataTable, type ColumnDef } from "@/components/data-table";
import { formatMoney, formatReportDate } from "@/components/reports";
import { Badge, Button, ErrorBanner, FadeIn } from "@/components/ui";

export default function CreditNotesPage() {
  const router = useRouter();
  const notes = useQuery({ queryKey: ["credit-notes"], queryFn: getCreditNotes });
  const error = notes.error as ApiError | null;

  return (
    <FadeIn className="space-y-6">
      <PageHeader
        title="Credit notes"
        description="Every credit note raised in the new system, newest first. A credit note reverses part or all of an invoice, reducing what the customer owes."
      />

      {error && <ErrorBanner message={error.message} correlationId={error.correlationId} />}

      <DataTable
        columns={columns}
        rows={notes.data}
        loading={notes.isPending}
        searchable={(row) => `${row.number} ${row.invoiceNumber} ${row.customerName ?? ""}`}
        searchPlaceholder="Search by number, invoice or customer…"
        defaultSort={{ id: "number", desc: true }}
        actions={
          <Button size="sm" onClick={() => router.push("/credit-notes/new")}>
            <Plus />
            New credit note
          </Button>
        }
        onRowClick={(row) => router.push(`/credit-notes/${row.id}`)}
        empty={{
          title: "No credit notes yet",
          description: "Credit notes raised in the new system appear here.",
        }}
      />
    </FadeIn>
  );
}

const columns: ColumnDef<CreditNoteSummary, unknown>[] = [
  {
    id: "number",
    accessorFn: (row) => row.number,
    header: "Number",
    cell: ({ row }) => (
      <span className="flex items-center gap-2">
        <span className="font-medium text-text">{row.original.number}</span>
        {row.original.origin === "legacy" && <Badge tone="neutral">Legacy</Badge>}
      </span>
    ),
  },
  {
    id: "date",
    accessorFn: (row) => row.date,
    header: "Date",
    cell: ({ row }) => <span className="whitespace-nowrap text-muted">{formatReportDate(row.original.date)}</span>,
  },
  {
    id: "invoice",
    accessorFn: (row) => row.invoiceNumber,
    header: "Credits invoice",
    cell: ({ row }) => <span className="font-mono text-xs text-muted">{row.original.invoiceNumber}</span>,
  },
  {
    id: "customer",
    accessorFn: (row) => row.customerName ?? "",
    header: "Customer",
    cell: ({ row }) => <span className="text-text">{row.original.customerName ?? "—"}</span>,
  },
  {
    id: "total",
    accessorFn: (row) => row.total,
    header: "Amount",
    meta: { align: "right" },
    cell: ({ row }) => <span className="tabular font-medium text-text">{formatMoney(row.original.total)}</span>,
  },
];
