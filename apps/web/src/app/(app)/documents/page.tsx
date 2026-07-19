"use client";

/**
 * Document storage (Phase 7, slice 4) — the company's file library.
 *
 * Files live on the server's disk, outside anything a web server serves, and the only way to one is the
 * download link here, which checks the permission and the company first. The legacy app put uploads under
 * the site directory, where a guessed filename was enough (Finding C3).
 */

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Download, FileText, Trash2, Upload } from "lucide-react";
import { useRef, useState } from "react";
import { ApiError } from "@/lib/api";
import {
  ACCEPTED_FILE_TYPES,
  MAX_UPLOAD_BYTES,
  deleteDocument,
  documentContentUrl,
  formatFileSize,
  getDocuments,
  uploadDocument,
  type DocumentSummary,
} from "@/lib/documents";
import { PageHeader } from "@/components/shell/app-shell";
import { DataTable, type ColumnDef } from "@/components/data-table";
import { formatReportDate } from "@/components/reports";
import { useReason } from "@/components/form";
import { Button, ErrorBanner, FadeIn, Input, toast } from "@/components/ui";

export default function DocumentsPage() {
  const [title, setTitle] = useState("");
  const fileInput = useRef<HTMLInputElement>(null);

  const queryClient = useQueryClient();
  const { ask, dialog: reasonDialog } = useReason();

  const documents = useQuery({ queryKey: ["documents"], queryFn: () => getDocuments() });

  const upload = useMutation({
    mutationFn: (file: File) => uploadDocument({ file, title }),
    onSuccess: (created) => {
      toast.success(`${created.title} uploaded.`);
      setTitle("");
      if (fileInput.current) fileInput.current.value = "";
      void queryClient.invalidateQueries({ queryKey: ["documents"] });
    },
    onError: (error: unknown) =>
      toast.error(error instanceof ApiError ? error.message : "The upload failed."),
  });

  const remove = useMutation({
    mutationFn: (v: { row: DocumentSummary; reason: string }) =>
      deleteDocument(v.row.id, v.row.rowVersion, v.reason),
    onSuccess: () => {
      toast.success("Document removed.");
      void queryClient.invalidateQueries({ queryKey: ["documents"] });
    },
    onError: (error: unknown) =>
      toast.error(error instanceof ApiError ? error.message : "The document could not be removed."),
  });

  const onFileChosen = (file: File | undefined) => {
    if (!file) return;

    // Checked here so the answer is immediate, and again on the server, which is the authority. A 25 MB
    // upload that fails on arrival has already cost the person their connection.
    if (file.size > MAX_UPLOAD_BYTES) {
      toast.error(`${file.name} is ${formatFileSize(file.size)}. The limit is 25 MB.`);
      if (fileInput.current) fileInput.current.value = "";
      return;
    }

    upload.mutate(file);
  };

  const loadError = documents.error as ApiError | null;

  const columns: ColumnDef<DocumentSummary, unknown>[] = [
    {
      id: "title",
      header: "Title",
      accessorFn: (row) => row.title,
      cell: ({ row }) => (
        <span className="flex items-center gap-2">
          <FileText className="size-4 shrink-0 text-muted" aria-hidden />
          <span className="text-text">{row.original.title}</span>
        </span>
      ),
    },
    {
      id: "originalFileName",
      header: "File",
      accessorFn: (row) => row.originalFileName,
      cell: ({ row }) => <span className="text-muted">{row.original.originalFileName}</span>,
    },
    {
      id: "byteSize",
      header: "Size",
      accessorFn: (row) => row.byteSize,
      meta: { align: "right" },
      cell: ({ row }) => <span className="tabular text-muted">{formatFileSize(row.original.byteSize)}</span>,
    },
    {
      id: "uploadedAt",
      header: "Uploaded",
      accessorFn: (row) => row.uploadedAt,
      cell: ({ row }) => (
        <span className="whitespace-nowrap text-muted">{formatReportDate(row.original.uploadedAt)}</span>
      ),
    },
    {
      id: "actions",
      header: "",
      enableSorting: false,
      meta: { align: "right" },
      cell: ({ row }) => (
        <span className="flex items-center justify-end gap-1">
          {/*
            A plain link, not a fetch: the browser takes the filename from the Content-Disposition
            header, which a blob assembled in JavaScript would lose.
          */}
          <a
            href={documentContentUrl(row.original.id)}
            className="inline-flex items-center gap-1.5 rounded-md px-2 py-1 text-sm text-text hover:bg-surface-sunken"
            title={`Download ${row.original.originalFileName}`}
          >
            <Download className="size-4" aria-hidden />
            <span className="sr-only">Download</span>
          </a>

          <button
            type="button"
            className="inline-flex items-center gap-1.5 rounded-md px-2 py-1 text-sm text-danger-text hover:bg-surface-sunken"
            title="Remove"
            onClick={() =>
              ask({
                title: "Why is this being removed?",
                description: `${row.original.title} will be removed, and the file deleted from storage.`,
                confirmLabel: "Remove document",
                onConfirm: (why) => remove.mutateAsync({ row: row.original, reason: why }),
              })
            }
          >
            <Trash2 className="size-4" aria-hidden />
            <span className="sr-only">Remove</span>
          </button>
        </span>
      ),
    },
  ];

  return (
    <FadeIn className="space-y-6">
      <PageHeader
        title="Documents"
        description="The company's files — stored off the web root and reachable only through this screen."
      />

      {loadError && <ErrorBanner message={loadError.message} correlationId={loadError.correlationId} />}

      <div className="flex flex-wrap items-end gap-3 rounded-lg border border-subtle bg-surface p-4">
        <Input
          label="Title (optional)"
          value={title}
          onChange={(e) => setTitle(e.target.value)}
          placeholder="Defaults to the file's name"
          className="min-w-[16rem] flex-1"
        />

        <input
          ref={fileInput}
          type="file"
          accept={ACCEPTED_FILE_TYPES}
          className="hidden"
          onChange={(e) => onFileChosen(e.target.files?.[0])}
        />

        <Button pending={upload.isPending} onClick={() => fileInput.current?.click()}>
          <Upload className="size-4" aria-hidden />
          Upload a file
        </Button>
      </div>

      <p className="text-xs text-muted">
        PDF, Word, Excel, CSV, text and images, up to 25 MB. Anything else is refused.
      </p>

      <DataTable
        columns={columns}
        rows={documents.data}
        loading={documents.isPending}
        defaultSort={{ id: "uploadedAt", desc: true }}
        searchable={(r) => `${r.title} ${r.originalFileName}`}
        searchPlaceholder="Search documents…"
        empty={{
          title: "No documents yet",
          description: "Upload a file and it appears here.",
        }}
      />

      {reasonDialog}
    </FadeIn>
  );
}
