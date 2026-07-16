import type {
  CreateSupplierInvoiceRequest,
  RecordSupplierPaymentRequest,
  SupplierInvoiceCreatedResponse,
  SupplierInvoiceDetail,
  SupplierInvoiceSummary,
  SupplierPaymentRecordedResponse,
} from "@smartnet/api-client";
import { api } from "./api";

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
export const getSupplierInvoices = () => api<SupplierInvoiceSummary[]>("/api/supplier-invoices");

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
