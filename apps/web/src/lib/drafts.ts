/**
 * Unraised work on the four create screens — autosaved to the server, resumable from any browser.
 *
 * A draft is not a document. It takes no number, posts nothing to the ledger, moves no stock, and the
 * legacy app cannot see it; it is the create screen's own state, kept somewhere a closed tab cannot
 * reach. Raising it goes through the ordinary create call, and the draft is then cleared.
 */

import type { DraftDetail, DraftSaved, DraftSummary, SaveDraftRequest } from "@smartnet/api-client";
import { api } from "./api";

// Generated from the API's OpenAPI schema — see packages/api-client. Re-exported, never redeclared.
export type { DraftDetail, DraftSaved, DraftSummary, SaveDraftRequest } from "@smartnet/api-client";

/**
 * The four screens that keep drafts, named as the server names them.
 *
 * These are the server's `DocumentTypes` constants, not a parallel vocabulary — a draft's type is
 * checked against `DraftDocumentTypes.PermissionByType` on every call, so a value invented here would
 * be a 400 rather than a quiet mismatch.
 */
export const DRAFT_QUOTATION = "QUOTATION";
export const DRAFT_INVOICE = "INVOICE";
export const DRAFT_PURCHASE_ORDER = "PO";
export const DRAFT_JOB_CARD = "JOBCARD";

export type DraftDocType =
  | typeof DRAFT_QUOTATION
  | typeof DRAFT_INVOICE
  | typeof DRAFT_PURCHASE_ORDER
  | typeof DRAFT_JOB_CARD;

/** The active company's unraised drafts of one type, most recently touched first. */
export const listDrafts = (docType: DraftDocType) =>
  api<DraftSummary[]>(`/api/drafts?docType=${docType}`);

/** One draft in full, with the state to load back into the create screen. */
export const getDraft = (id: number) => api<DraftDetail>(`/api/drafts/${id}`);

/** Starts a draft — the first autosave a create screen makes. */
export const createDraft = (request: SaveDraftRequest, keepalive = false) =>
  api<DraftSaved>("/api/drafts", { method: "POST", body: request, keepalive });

/**
 * Every autosave after the first. A stale `expectedRowVersion` is a 409 — drafts are shared, and the
 * screen must say so rather than overwrite whoever else has it open.
 */
export const updateDraft = (
  id: number,
  expectedRowVersion: number,
  request: SaveDraftRequest,
  keepalive = false,
) =>
  api<DraftSaved>(`/api/drafts/${id}?expectedRowVersion=${expectedRowVersion}`, {
    method: "PUT",
    body: request,
    keepalive,
  });

/** Discards a draft, or clears it once its document has been raised. Already-gone is not an error. */
export const deleteDraft = (id: number) => api<void>(`/api/drafts/${id}`, { method: "DELETE" });

/**
 * The shape a create screen serialises into a draft.
 *
 * `v` is the shape's version, not the row's. A draft written by an older deployment may be missing
 * fields a newer screen requires, or hold a field that has since changed meaning; loading it blind
 * would half-fill a form with plausible nonsense, which is worse than not resuming at all. So the
 * screen stamps the version it wrote, and `readPayload` refuses anything it does not recognise.
 */
export interface DraftPayload<T> {
  v: number;
  state: T;
}

export const writePayload = <T>(version: number, state: T): string =>
  JSON.stringify({ v: version, state } satisfies DraftPayload<T>);

/**
 * The state inside a draft, or null when it cannot be trusted — a payload from a shape this screen no
 * longer understands, or one that is not what it claims to be.
 *
 * Null is a real outcome, not an error case to log and forget: the caller shows "this draft was saved
 * by an older version and cannot be opened" and leaves the row alone, so nothing is silently discarded
 * and nothing is silently misread.
 */
export function readPayload<T>(payload: string, version: number): T | null {
  try {
    const parsed = JSON.parse(payload) as Partial<DraftPayload<T>>;

    if (parsed.v !== version || parsed.state == null || typeof parsed.state !== "object") {
      return null;
    }

    return parsed.state as T;
  } catch {
    return null;
  }
}

/** The draft id a create screen was opened to resume, from `?draft=`. */
export function draftIdFromLocation(): number | null {
  if (typeof window === "undefined") return null;

  const raw = new URLSearchParams(window.location.search).get("draft");
  const parsed = raw === null ? Number.NaN : Number(raw);

  return Number.isInteger(parsed) && parsed > 0 ? parsed : null;
}
