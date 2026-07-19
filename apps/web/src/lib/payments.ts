import type {
  CreateCustomerReceiptRequest,
  CustomerReceiptCreatedResponse,
  CustomerReceiptDetail,
  CustomerReceiptSummary,
  OutstandingInvoiceLine,
} from "@smartnet/api-client";
import { api } from "./api";
import type { Paged } from "./paging";

// Generated from the API's OpenAPI schema — see packages/api-client. Re-exported, never redeclared.
export type {
  CreateCustomerReceiptRequest,
  CreateReceiptAllocationRequest,
  CustomerReceiptCreatedResponse,
  CustomerReceiptDetail,
  CustomerReceiptSummary,
  OutstandingInvoiceLine,
  ReceiptAllocationLine,
} from "@smartnet/api-client";

/** The receipts this app has recorded, newest first. */
/**
 * One page of receipts, ordered across both origins by the server.
 *
 * This list merges two different tables (2,226 legacy payments plus what this app records), so the server decides the
 * order over both before taking a page — paging one and merging after would drop or repeat rows.
 */
export const getCustomerReceipts = (params: { page: number; pageSize?: number; search?: string }) => {
  const query = new URLSearchParams({
    page: String(params.page),
    pageSize: String(params.pageSize ?? 25),
  });

  if (params.search?.trim()) query.set("search", params.search.trim());

  return api<Paged<CustomerReceiptSummary>>(`/api/customer-receipts?${query}`);
};

/** One receipt in full, with its per-invoice allocations. */
export const getCustomerReceipt = (id: number) => api<CustomerReceiptDetail>(`/api/customer-receipts/${id}`);

/** A customer's open invoices — the picker a receipt is allocated over (new and legacy alike). */
export const getOutstandingInvoices = (customerId: number) =>
  api<OutstandingInvoiceLine[]>(`/api/customer-receipts/outstanding?customerId=${customerId}`);

/** Record a receipt — allocated across open invoices; posts Payment entries and dual-writes the legacy shadow. */
export const createCustomerReceipt = (request: CreateCustomerReceiptRequest) =>
  api<CustomerReceiptCreatedResponse>("/api/customer-receipts", { method: "POST", body: request });

/** Void a receipt — soft, reason-gated. Reverses each allocation through a compensating entry. A stale row_version is a 409. */
export const voidCustomerReceipt = (id: number, expectedRowVersion: number, reason: string) =>
  api<void>(`/api/customer-receipts/${id}?expectedRowVersion=${expectedRowVersion}`, { method: "DELETE", reason });
