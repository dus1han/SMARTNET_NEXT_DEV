import type {
  CreateSupplierInvoiceRequest,
  RecordSupplierPaymentRequest,
  SupplierInvoiceCreatedResponse,
  SupplierInvoiceDetail,
  SupplierInvoiceSummary,
  SupplierPaymentRecordedResponse,
} from "@smartnet/api-client";
import { api } from "./api";
import type { Paged } from "./paging";

// Generated from the API's OpenAPI schema — see packages/api-client. Re-exported, never redeclared.
export type {
  CreateSupplierInvoiceRequest,
  RecordSupplierPaymentRequest,
  SupplierInvoiceCreatedResponse,
  SupplierInvoiceDetail,
  SupplierInvoicePaymentLine,
  SupplierInvoiceSummary,
} from "@smartnet/api-client";

/** The supplier invoices this app has recorded and the legacy ones adopted, newest first. */
/**
 * One page of supplier invoices, searched and ordered by the server (1,677 rows).
 *
 * The whole list used to come back so the browser could page it; the server pages it now, which also
 * means the search box searches every row rather than the page that happens to be loaded.
 */
export const getSupplierInvoices = (params: { page: number; pageSize?: number; search?: string }) => {
  const query = new URLSearchParams({
    page: String(params.page),
    pageSize: String(params.pageSize ?? 25),
  });

  if (params.search?.trim()) query.set("search", params.search.trim());

  return api<Paged<SupplierInvoiceSummary>>(`/api/supplier-invoices?${query}`);
};

/** One supplier invoice in full, with its derived outstanding and payment history. */
export const getSupplierInvoice = (id: number) => api<SupplierInvoiceDetail>(`/api/supplier-invoices/${id}`);

/** Record a supplier invoice — a header-only AP record; posts the payable to the payables ledger. */
export const createSupplierInvoice = (request: CreateSupplierInvoiceRequest) =>
  api<SupplierInvoiceCreatedResponse>("/api/supplier-invoices", { method: "POST", body: request });

/** Record a (partial) payment — a ledger entry. "Paid" becomes true when the derived outstanding hits zero. */
export const recordSupplierPayment = (id: number, request: RecordSupplierPaymentRequest) =>
  api<SupplierPaymentRecordedResponse>(`/api/supplier-invoices/${id}/payments`, { method: "POST", body: request });

/** Void a supplier invoice — soft, reason-gated. A stale row_version is a 409. */
export const deleteSupplierInvoice = (id: number, expectedRowVersion: number, reason: string) =>
  api<void>(`/api/supplier-invoices/${id}?expectedRowVersion=${expectedRowVersion}`, { method: "DELETE", reason });
