import type {
  CreateCustomerReceiptRequest,
  CustomerReceiptCreatedResponse,
  CustomerReceiptDetail,
  CustomerReceiptSummary,
  OutstandingInvoiceLine,
} from "@smartnet/api-client";
import { api } from "./api";

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
export const getCustomerReceipts = () => api<CustomerReceiptSummary[]>("/api/customer-receipts");

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
