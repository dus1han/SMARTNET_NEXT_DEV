import type {
  ConvertQuotationRequest,
  EmailDocumentRequest,
  EmailDocumentResponse,
  InvoiceCreatedResponse,
  ProformaCreatedResponse,
  ProformaDetail,
  ProformaSummary,
  ProformaVoided,
  QuotationRecipients,
} from "@smartnet/api-client";
import { api } from "./api";
import type { Paged } from "./paging";

// Generated from the API's OpenAPI schema — see packages/api-client. Re-exported, never redeclared.
export type { ProformaDetail, ProformaSummary } from "@smartnet/api-client";

/** One page of proforma invoices, newest first — the live ones, or the voided ones when `voided` is set. */
export const getProformas = (params: { page: number; pageSize?: number; search?: string; voided?: boolean }) => {
  const query = new URLSearchParams({
    page: String(params.page),
    pageSize: String(params.pageSize ?? 25),
  });

  if (params.search?.trim()) query.set("search", params.search.trim());
  if (params.voided) query.set("voided", "true");

  return api<Paged<ProformaSummary>>(`/api/proforma-invoices?${query}`);
};

/** One proforma in full, voided ones included. */
export const getProforma = (id: number) => api<ProformaDetail>(`/api/proforma-invoices/${id}`);

/** Raise a proforma from a quotation. One live proforma per quotation; a second is refused (409). */
export const createProforma = (quotationId: number, date: string) =>
  api<ProformaCreatedResponse>("/api/proforma-invoices", { method: "POST", body: { quotationId, date } });

/** Convert the proforma into an invoice — once only, through the ordinary invoice pipeline. */
export const convertProforma = (id: number, request: ConvertQuotationRequest) =>
  api<InvoiceCreatedResponse>(`/api/proforma-invoices/${id}/convert`, { method: "POST", body: request });

/** Void a proforma — soft, reason-gated. Frees its quotation. A converted one is refused (409). */
export const voidProforma = (id: number, expectedRowVersion: number, reason: string) =>
  api<ProformaVoided>(`/api/proforma-invoices/${id}?expectedRowVersion=${expectedRowVersion}`, { method: "DELETE", reason });

/** Who this proforma can be emailed to, and the covering message. */
export const proformaRecipients = (id: number) => api<QuotationRecipients>(`/api/proforma-invoices/${id}/recipients`);

/** Emails the proforma as a PDF attachment to the chosen saved contacts. */
export const emailProforma = (id: number, request: EmailDocumentRequest) =>
  api<EmailDocumentResponse>(`/api/proforma-invoices/${id}/email`, { method: "POST", body: request });
