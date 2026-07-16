import type {
  CreateCreditNoteRequest,
  CreditNoteCreatedResponse,
  CreditNoteDetail,
  CreditNoteSummary,
} from "@smartnet/api-client";
import { api } from "./api";

// Generated from the API's OpenAPI schema — see packages/api-client. Re-exported, never redeclared.
export type {
  CreateCreditNoteRequest,
  CreditNoteCreatedResponse,
  CreditNoteDetail,
  CreditNoteSummary,
} from "@smartnet/api-client";

/** The credit notes this app has raised, newest first. */
export const getCreditNotes = () => api<CreditNoteSummary[]>("/api/credit-notes");

/** One credit note in full, with its lines and its parent invoice. */
export const getCreditNote = (id: number) => api<CreditNoteDetail>(`/api/credit-notes/${id}`);

/**
 * Raise a credit note against a parent invoice — the whole document, posted once. It posts a Credit
 * ledger entry (reducing the customer's balance) and, when it returns goods, a stock receipt. The
 * customer, company and VAT rate are inherited from the parent invoice, so a full credit nets against it.
 */
export const createCreditNote = (request: CreateCreditNoteRequest) =>
  api<CreditNoteCreatedResponse>("/api/credit-notes", { method: "POST", body: request });
