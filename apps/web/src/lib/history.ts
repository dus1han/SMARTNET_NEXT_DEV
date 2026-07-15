import type {
  AuditEntry,
  DocumentVersionDetail,
  DocumentVersionSummary,
  RecordHistory,
} from "@smartnet/api-client";
import { api } from "./api";

// Generated from the API's OpenAPI schema — see packages/api-client. Re-exported, never redeclared.
export type { AuditEntry, DocumentVersionDetail, DocumentVersionSummary, RecordHistory };

/**
 * The history of one record: every change the audit log holds about it, newest first.
 *
 * `entityType` is the CLR entity name the server writes into `audit_log.entity_type` — "User",
 * "Invoice", "TaxRate" — and `entityId` its key. An unknown type is a 404, not an empty list: on a
 * screen whose entire job is to be believed, a typo must not look like a clean history.
 */
export const recordHistory = (entityType: string, entityId: string | number, limit?: number) =>
  api<RecordHistory>(
    `/api/history/records/${encodeURIComponent(entityType)}/${encodeURIComponent(entityId)}` +
      (limit ? `?limit=${limit}` : ""),
  );

/** The versions of a document, without their snapshots — Phase 5 onwards. */
export const documentVersions = (docType: string, docId: number) =>
  api<DocumentVersionSummary[]>(
    `/api/history/documents/${encodeURIComponent(docType)}/${docId}/versions`,
  );

/** One version, snapshot included — what the diff and the reprint both read. */
export const documentVersion = (docType: string, docId: number, versionNo: number) =>
  api<DocumentVersionDetail>(
    `/api/history/documents/${encodeURIComponent(docType)}/${docId}/versions/${versionNo}`,
  );

/** One field's before and after. `from` is absent on a create: there was nothing there before. */
export interface FieldChange {
  from?: unknown;
  to?: unknown;
}

/**
 * The stored diff, parsed.
 *
 * The API hands `changes` over as the JSON string the database holds, rather than as an object,
 * because its shape is the shape of whichever entity changed — there is no schema for it, and
 * pretending there is one would produce a generated type of `Record<string, never>`. Parsing it
 * here, once, is the honest version.
 *
 * A row whose JSON cannot be parsed yields no fields rather than throwing: one corrupt audit row
 * must not take down the history of every other change to the record.
 */
export function parseChanges(changes: string | null | undefined): Record<string, FieldChange> {
  if (!changes) return {};

  try {
    const parsed: unknown = JSON.parse(changes);

    return parsed !== null && typeof parsed === "object" && !Array.isArray(parsed)
      ? (parsed as Record<string, FieldChange>)
      : {};
  } catch {
    return {};
  }
}

/**
 * What the server writes in place of a secret — `AuditRedaction.Placeholder`.
 *
 * The log records *that* a password changed, never what to. The history screen shows it as such
 * rather than printing the placeholder verbatim, which reads like a value.
 */
export const REDACTED = "***REDACTED***";
