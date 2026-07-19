"use client";

/**
 * Document storage (Phase 7, slice 4) — the company's file library.
 *
 * Files live on the server's disk, outside anything a web server serves, and the only way to one is the
 * download link here, which checks the permission and the company first. The legacy app put uploads under
 * the site directory, where a guessed filename was enough (Finding C3).
 */

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Download, Eye, FileText, Trash2, Upload } from "lucide-react";
import { useRef, useState } from "react";
import { ApiError } from "@/lib/api";
import {
  ACCEPTED_FILE_TYPES,
  MAX_UPLOAD_BYTES,
  canPreview,
  deleteDocument,
  documentContentUrl,
  documentPreviewUrl,
  formatFileSize,
  getDocuments,
  uploadDocument,
  type DocumentSummary,
} from "@/lib/documents";
import { PageHeader } from "@/components/shell/app-shell";
import { DataTable, type ColumnDef } from "@/components/data-table";
import { formatReportDate } from "@/components/reports";
import { useReason } from "@/components/form";
import { Button, Dialog, ErrorBanner, FadeIn, Input, toast } from "@/components/ui";

export default function DocumentsPage() {
  const [uploadOpen, setUploadOpen] = useState(false);
  const [title, setTitle] = useState("");
  const [file, setFile] = useState<File | null>(null);
  const [previewing, setPreviewing] = useState<DocumentSummary | null>(null);
  const fileInput = useRef<HTMLInputElement>(null);

  const queryClient = useQueryClient();
  const { ask, dialog: reasonDialog } = useReason();

  const documents = useQuery({ queryKey: ["documents"], queryFn: () => getDocuments() });

  const closeUpload = () => {
    setUploadOpen(false);
    setTitle("");
    setFile(null);
    if (fileInput.current) fileInput.current.value = "";
  };

  const upload = useMutation({
    mutationFn: () => uploadDocument({ file: file!, title }),
    onSuccess: (created) => {
      toast.success(`${created.title} uploaded.`);
      closeUpload();
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

  const onFileChosen = (chosen: File | undefined) => {
    if (!chosen) return;

    // Checked here so the answer is immediate, and again on the server, which is the authority. A 25 MB
    // upload that fails on arrival has already cost the person their connection.
    if (chosen.size > MAX_UPLOAD_BYTES) {
      toast.error(`${chosen.name} is ${formatFileSize(chosen.size)}. The limit is 25 MB.`);
      if (fileInput.current) fileInput.current.value = "";
      return;
    }

    setFile(chosen);

    // The filename is the obvious first draft of the title, and usually the right one. Only offered
    // when the box is untouched, so it never overwrites something typed.
    setTitle((current) => (current.trim() ? current : chosen.name.replace(/\.[^.]+$/, "")));
  };

  const loadError = documents.error as ApiError | null;

  const columns: ColumnDef<DocumentSummary, unknown>[] = [
    {
      id: "title",
      header: "Title",
      accessorFn: (row) => row.title,
      cell: ({ row }) =>
        // The title opens the preview where there is one to open, because clicking the name of a
        // document is what people try first. Where there is not, it stays plain text rather than
        // becoming a button that does nothing.
        canPreview(row.original.contentType) ? (
          <button
            type="button"
            onClick={() => setPreviewing(row.original)}
            className="flex items-center gap-2 text-left text-text underline-offset-2 hover:underline"
          >
            <FileText className="size-4 shrink-0 text-muted" aria-hidden />
            {row.original.title}
          </button>
        ) : (
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
          {canPreview(row.original.contentType) && (
            <button
              type="button"
              className="inline-flex items-center gap-1.5 rounded-md px-2 py-1 text-sm text-text hover:bg-surface-sunken"
              title={`Preview ${row.original.title}`}
              onClick={() => setPreviewing(row.original)}
            >
              <Eye className="size-4" aria-hidden />
              <span className="sr-only">Preview</span>
            </button>
          )}

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
      {/*
        The upload lives in the header, where every other list screen puts its primary action. It had
        its own panel above the table, and that panel was the single structural difference between this
        page and the ones that fit — 126px plus a gap, on a page that otherwise reads header, search,
        table. A document's title defaults to its filename, which is what people name files anyway.
      */}
      <PageHeader
        title="Documents"
        description="The company's files — stored off the web root and reachable only through this screen."
        actions={
          <Button onClick={() => setUploadOpen(true)}>
            <Upload className="size-4" aria-hidden />
            Upload a file
          </Button>
        }
      />

      {loadError && <ErrorBanner message={loadError.message} correlationId={loadError.correlationId} />}

      <DataTable
        columns={columns}
        rows={documents.data}
        loading={documents.isPending}
        // 10, not the default 25. At ~51px a row, 25 rows is 1,275px of table on a page that also
        // carries a header and an upload panel — so the default guaranteed the page scrolled, and
        // scrolled past the end of its own content. Ten rows fit a laptop viewport whole.
        pageSize={10}
        defaultSort={{ id: "uploadedAt", desc: true }}
        searchable={(r) => `${r.title} ${r.originalFileName}`}
        searchPlaceholder="Search documents…"
        empty={{
          title: "No documents yet",
          description: "Upload a file and it appears here.",
        }}
      />

      <Dialog
        open={uploadOpen}
        onOpenChange={(next) => !next && closeUpload()}
        title="Upload a document"
        description="PDF, Word, Excel, CSV, text and images, up to 25 MB. Anything else is refused."
        footer={
          <>
            <Button variant="ghost" onClick={closeUpload}>
              Cancel
            </Button>
            <Button
              pending={upload.isPending}
              // Both, not either: a document with no title is one nobody finds again, and the server
              // refuses it anyway.
              disabled={!file || title.trim().length === 0}
              onClick={() => upload.mutate()}
            >
              Upload
            </Button>
          </>
        }
      >
        <div className="space-y-4">
          <input
            ref={fileInput}
            type="file"
            accept={ACCEPTED_FILE_TYPES}
            className="hidden"
            onChange={(e) => onFileChosen(e.target.files?.[0])}
          />

          <div>
            <p className="mb-1.5 text-sm font-medium text-text">File</p>
            <div className="flex flex-wrap items-center gap-3">
              <Button variant="secondary" onClick={() => fileInput.current?.click()}>
                {file ? "Choose a different file" : "Choose a file"}
              </Button>

              {file && (
                <span className="min-w-0 text-sm text-muted">
                  <span className="text-text">{file.name}</span> · {formatFileSize(file.size)}
                </span>
              )}
            </div>
          </div>

          <Input
            label="Title"
            required
            value={title}
            onChange={(e) => setTitle(e.target.value)}
            placeholder="What this document is"
            hint="The name this document is listed and searched under."
          />
        </div>
      </Dialog>

      <Dialog
        open={previewing !== null}
        onOpenChange={(next) => !next && setPreviewing(null)}
        size="lg"
        title={previewing?.title ?? ""}
        description={
          previewing
            ? `${previewing.originalFileName} · ${formatFileSize(previewing.byteSize)}`
            : undefined
        }
        footer={
          previewing && (
            <>
              <Button variant="ghost" onClick={() => setPreviewing(null)}>
                Close
              </Button>
              <a
                href={documentContentUrl(previewing.id)}
                className="inline-flex items-center gap-2 rounded-md bg-primary px-3 py-2 text-sm font-medium text-primary-text shadow-sm shadow-primary/25 hover:bg-primary-hover"
              >
                <Download className="size-4" aria-hidden />
                Download
              </a>
            </>
          )
        }
      >
        {previewing && <DocumentPreview document={previewing} />}
      </Dialog>

      {reasonDialog}
    </FadeIn>
  );
}

/**
 * The preview body — an image or a PDF, rendered from the inline endpoint.
 *
 * <b>An iframe rather than a fetched blob.</b> A blob URL would mean holding the whole file in memory
 * and re-implementing the PDF viewer's chrome; the browser already has one, and pointing it at a URL
 * that streams costs nothing. The auth cookie rides along because the request is same-origin in
 * production, where nginx serves the API and the app from one host.
 */
function DocumentPreview({ document }: { document: DocumentSummary }) {
  const url = documentPreviewUrl(document.id);

  if (document.contentType.startsWith("image/")) {
    return (
      <div className="flex max-h-[70vh] justify-center overflow-auto rounded-lg bg-surface-sunken p-2">
        {/* eslint-disable-next-line @next/next/no-img-element -- a streamed private file, not a static asset the optimiser can reach */}
        <img src={url} alt={document.title} className="max-h-full max-w-full object-contain" />
      </div>
    );
  }

  return (
    <iframe
      src={url}
      title={document.title}
      className="h-[70vh] w-full rounded-lg border border-subtle bg-surface-sunken"
    />
  );
}
