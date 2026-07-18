import type {
  ConvertQuotationRequest,
  CreateQuotationRequest,
  EditQuotationRequest,
  EmailDocumentRequest,
  EmailDocumentResponse,
  InvoiceCreatedResponse,
  InvoiceTaxRate,
  QuotationCreatedResponse,
  QuotationDeleted,
  QuotationDetail,
  QuotationEditedResponse,
  QuotationRecipients,
  QuotationSummary,
} from "@smartnet/api-client";
import { api } from "./api";

// Generated from the API's OpenAPI schema — see packages/api-client. Re-exported, never redeclared.
export type {
  ConvertQuotationRequest,
  CreateQuotationRequest,
  EditQuotationRequest,
  EmailDocumentRequest,
  EmailDocumentResponse,
  QuotationCreatedResponse,
  QuotationDetail,
  QuotationRecipients,
  QuotationSummary,
} from "@smartnet/api-client";

/** The quotations this app has raised, newest first. */
export const getQuotations = () => api<QuotationSummary[]>("/api/quotations");

/** One quotation in full, with its lines and conversion state. */
export const getQuotation = (id: number) => api<QuotationDetail>(`/api/quotations/${id}`);

/**
 * The one VAT rate a quotation raised for this company on this date will carry — the same server engine
 * the save uses, gated by the quotation permission. Fetched once when company or date changes.
 */
export const getQuotationTaxRate = (companyId: number, date: string) =>
  api<InvoiceTaxRate>(`/api/quotations/tax-rate?companyId=${companyId}&date=${date}`);

/** Raise a quotation — the whole document, posted once. No ledger, no stock. */
export const createQuotation = (request: CreateQuotationRequest) =>
  api<QuotationCreatedResponse>("/api/quotations", { method: "POST", body: request });

/**
 * Convert a quotation into an invoice — through the same save pipeline a hand-keyed invoice uses, once
 * only. Returns the new invoice; a second attempt is refused by the server (409).
 */
export const convertQuotation = (id: number, request: ConvertQuotationRequest) =>
  api<InvoiceCreatedResponse>(`/api/quotations/${id}/convert`, { method: "POST", body: request });

/** Edit a quotation — versioned, reason-gated. A legacy quote is adopted; a converted one is refused (409). */
export const editQuotation = (id: number, request: EditQuotationRequest, reason: string) =>
  api<QuotationEditedResponse>(`/api/quotations/${id}`, { method: "PUT", body: request, reason });

/** Void a quotation — soft, recoverable, reason-gated. A stale row_version is a 409. */
export const deleteQuotation = (id: number, expectedRowVersion: number, reason: string) =>
  api<QuotationDeleted>(`/api/quotations/${id}?expectedRowVersion=${expectedRowVersion}`, { method: "DELETE", reason });

/**
 * Who this quotation can be emailed to, and the covering message that would go with it.
 *
 * Works for a legacy quotation as readily as an adopted one — the contacts come from the customer's
 * saved records, resolved by the code the legacy document carries.
 */
export const quotationRecipients = (id: number) =>
  api<QuotationRecipients>(`/api/quotations/${id}/recipients`);

/**
 * Emails the quotation as a PDF attachment to the chosen saved contacts.
 *
 * Resolves 200 even when the mail server refused it — the response carries `sent` and the reason, so a
 * refusal is shown as a refusal, not as a failed request.
 */
export const emailQuotation = (id: number, request: EmailDocumentRequest) =>
  api<EmailDocumentResponse>(`/api/quotations/${id}/email`, { method: "POST", body: request });
