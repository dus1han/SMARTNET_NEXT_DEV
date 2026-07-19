import type { DocumentSummary } from "@smartnet/api-client";
import { api, API_BASE_URL } from "./api";

// Generated from the API's OpenAPI schema — see packages/api-client. Re-exported, never redeclared.
export type { DocumentSummary } from "@smartnet/api-client";

/** What the upload input accepts, and what the server's whitelist admits. Kept in step by hand. */
export const ACCEPTED_FILE_TYPES =
  ".pdf,.doc,.docx,.xls,.xlsx,.csv,.txt,.png,.jpg,.jpeg,.gif,.webp";

/** The server's cap. Mirrored here only so the form can say so before spending the upload. */
export const MAX_UPLOAD_BYTES = 25 * 1024 * 1024;

/**
 * Documents the caller may see — the whole library, or one record's attachments.
 *
 * Passing an entity narrows to what is attached to it; passing neither lists the company's library.
 */
export const getDocuments = (entity?: { type: string; id: number }) =>
  api<DocumentSummary[]>(
    entity
      ? `/api/documents?entityType=${encodeURIComponent(entity.type)}&entityId=${entity.id}`
      : "/api/documents",
  );

/**
 * Uploads a file against the active company.
 *
 * The type and size are checked here so the answer is immediate, and again on the server, which is the
 * authority — this check is a courtesy to the person uploading, not a control.
 */
export const uploadDocument = (input: {
  file: File;
  title?: string;
  entity?: { type: string; id: number };
}) => {
  const form = new FormData();
  form.append("file", input.file);

  if (input.title?.trim()) form.append("title", input.title.trim());

  if (input.entity) {
    form.append("entityType", input.entity.type);
    form.append("entityId", String(input.entity.id));
  }

  return api<DocumentSummary>("/api/documents", { method: "POST", body: form });
};

/**
 * Where the bytes come from.
 *
 * A plain URL rather than a fetch: the browser's own download handling gets the filename from the
 * Content-Disposition header, and a blob built in JavaScript would lose it.
 */
export const documentContentUrl = (id: number) => `${API_BASE_URL}/api/documents/${id}/content`;

/** Removes a document — soft on the row, and the file with it. Audited, so a reason is required. */
export const deleteDocument = (id: number, expectedRowVersion: number, reason: string) =>
  api<void>(`/api/documents/${id}?expectedRowVersion=${expectedRowVersion}`, {
    method: "DELETE",
    reason,
  });

/** Bytes as a person reads them. */
export const formatFileSize = (bytes: number) => {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${Math.round(bytes / 1024)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
};
