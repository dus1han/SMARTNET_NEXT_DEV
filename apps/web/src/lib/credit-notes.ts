import type {
  CreateCreditNoteRequest,
  CreditNoteCreatedResponse,
  CreditNoteDetail,
  CreditNoteDeleted,
  CreditNoteRecipients,
  CreditNoteSummary,
  EmailDocumentRequest,
  EmailDocumentResponse,
} from "@smartnet/api-client";
import { api } from "./api";

// Generated from the API's OpenAPI schema — see packages/api-client. Re-exported, never redeclared.
export type {
  CreateCreditNoteRequest,
  CreditNoteCreatedResponse,
  CreditNoteDetail,
  CreditNoteDeleted,
  CreditNoteRecipients,
  CreditNoteSummary,
  EmailDocumentRequest,
  EmailDocumentResponse,
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

/** Who this credit note can be emailed to — the customer's saved contacts, and why a send might fail. */
export const creditNoteRecipients = (id: number) =>
  api<CreditNoteRecipients>(`/api/credit-notes/${id}/recipients`);

/**
 * Emails the credit note as a PDF attachment to the chosen saved contacts.
 *
 * Resolves 200 even when the mail server refused it — the response carries `sent` and the reason.
 */
export const emailCreditNote = (id: number, request: EmailDocumentRequest) =>
  api<EmailDocumentResponse>(`/api/credit-notes/${id}/email`, { method: "POST", body: request });

/**
 * Voids a credit note — soft, recoverable, reason-gated. A stale row_version is a 409.
 *
 * The server reverses the ledger credit and any stock the note returned, through new entries. There is
 * no edit: a correction to a credit note is a new note, not a rewrite of one already sent.
 */
export const deleteCreditNote = (id: number, expectedRowVersion: number, reason: string) =>
  api<CreditNoteDeleted>(`/api/credit-notes/${id}?expectedRowVersion=${expectedRowVersion}`, {
    method: "DELETE",
    reason,
  });
