"use client";

/**
 * The job-card list.
 *
 * Service/repair jobs. No money in the list — a job card charges nothing; what it shows is where the job
 * is in the PENDING → CLOSED workflow.
 */

import { useQuery } from "@tanstack/react-query";
import { useRouter } from "next/navigation";
import { Plus } from "lucide-react";
import { ApiError } from "@/lib/api";
import { getJobCards, type JobCardSummary } from "@/lib/job-cards";
import { PageHeader } from "@/components/shell/app-shell";
import { DataTable, type ColumnDef } from "@/components/data-table";
import { formatReportDate } from "@/components/reports";
import { Badge, Button, ErrorBanner, FadeIn } from "@/components/ui";

export default function JobCardsPage() {
  const router = useRouter();
  const jobs = useQuery({ queryKey: ["job-cards"], queryFn: getJobCards });
  const error = jobs.error as ApiError | null;

  return (
    <FadeIn className="space-y-6">
      <PageHeader
        title="Job cards"
        description="Service and repair jobs, newest first — booked in, then closed once the work is done."
      />

      {error && <ErrorBanner message={error.message} correlationId={error.correlationId} />}

      <DataTable
        columns={columns}
        rows={jobs.data}
        loading={jobs.isPending}
        searchable={(row) => `${row.number} ${row.customerName ?? ""}`}
        searchPlaceholder="Search by number or customer…"
        defaultSort={{ id: "date", desc: true }}
        actions={
          <Button size="sm" onClick={() => router.push("/job-cards/new")}>
            <Plus />
            New job card
          </Button>
        }
        onRowClick={(row) => router.push(`/job-cards/${row.id}`)}
        empty={{ title: "No job cards yet", description: "Job cards raised in the new system appear here." }}
      />
    </FadeIn>
  );
}

const columns: ColumnDef<JobCardSummary, unknown>[] = [
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
    id: "customer",
    accessorFn: (row) => row.customerName ?? "",
    header: "Customer",
    cell: ({ row }) => <span className="text-text">{row.original.customerName ?? "—"}</span>,
  },
  {
    id: "status",
    accessorFn: (row) => row.status,
    header: "Status",
    cell: ({ row }) => (
      <Badge tone={row.original.status === "CLOSED" ? "success" : "warning"}>
        {row.original.status === "CLOSED" ? "Closed" : "Pending"}
      </Badge>
    ),
  },
];
