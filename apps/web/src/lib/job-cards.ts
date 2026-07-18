import type {
  CloseJobCardRequest,
  CreateJobCardRequest,
  EmailDocumentRequest,
  EmailDocumentResponse,
  JobCardCreatedResponse,
  JobCardDetail,
  JobCardSummary,
  JobSheetRecipients,
} from "@smartnet/api-client";
import { api } from "./api";

// Generated from the API's OpenAPI schema — see packages/api-client. Re-exported, never redeclared.
export type {
  CloseJobCardRequest,
  CreateJobCardRequest,
  EmailDocumentRequest,
  EmailDocumentResponse,
  JobCardCreatedResponse,
  JobCardDetail,
  JobCardLineDetail,
  JobCardSummary,
  DocumentContact,
  JobSheetRecipients,
} from "@smartnet/api-client";

/** The job cards this app has raised and the legacy ones adopted, newest first. */
export const getJobCards = () => api<JobCardSummary[]>("/api/job-cards");

/** One job card in full, with its serial-tracked lines and (once closed) cost/sell. */
export const getJobCard = (id: number) => api<JobCardDetail>(`/api/job-cards/${id}`);

/** Book in a job card — PENDING, with structured serial lines. No tax, no ledger, no stock. */
export const createJobCard = (request: CreateJobCardRequest) =>
  api<JobCardCreatedResponse>("/api/job-cards", { method: "POST", body: request });

/** Close a job — the guarded PENDING → CLOSED transition (closing means completed). A stale row_version is a 409. */
export const closeJobCard = (id: number, request: CloseJobCardRequest) =>
  api<void>(`/api/job-cards/${id}/close`, { method: "POST", body: request });

/**
 * Who the job sheet can be emailed to, and the message that would go with it.
 *
 * The server decides both — the contacts are the customer's saved ones, and `blocked` is why a send
 * would fail (no mail server, no contact with an address, sending switched off for the company). Asked
 * before the dialog opens so the user is told up front rather than after choosing recipients.
 */
export const jobSheetRecipients = (id: number) =>
  api<JobSheetRecipients>(`/api/job-cards/${id}/recipients`);

/**
 * Emails the job sheet as a PDF attachment to the chosen saved contacts.
 *
 * Resolves 200 even when the mail server refused it — the response carries `sent` and the reason, so a
 * refusal is shown as a refusal, not as a failed request. Only a real HTTP fault throws.
 */
export const emailJobSheet = (id: number, request: EmailDocumentRequest) =>
  api<EmailDocumentResponse>(`/api/job-cards/${id}/email`, { method: "POST", body: request });
