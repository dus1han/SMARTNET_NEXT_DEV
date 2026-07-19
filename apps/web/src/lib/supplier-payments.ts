import type {
  CreateSupplierPaymentRequest,
  SupplierPaymentCreatedResponse,
  SupplierPaymentDetail,
  SupplierPaymentSummary,
  OutstandingSupplierInvoiceLine,
} from "@smartnet/api-client";
import { api } from "./api";
import type { Paged } from "./paging";

// Generated from the API's OpenAPI schema — see packages/api-client. Re-exported, never redeclared.
export type {
  CreateSupplierPaymentRequest,
  CreateSupplierPaymentAllocationRequest,
  SupplierPaymentCreatedResponse,
  SupplierPaymentDetail,
  SupplierPaymentSummary,
  SupplierPaymentAllocationLine,
  OutstandingSupplierInvoiceLine,
} from "@smartnet/api-client";

/** The supplier payments this app has recorded, newest first. */
/**
 * One page of supplier payments, ordered across both origins by the server.
 *
 * This list merges two different tables (1,640 legacy settlements plus what this app records), so the server decides the
 * order over both before taking a page — paging one and merging after would drop or repeat rows.
 */
export const getSupplierPayments = (params: { page: number; pageSize?: number; search?: string }) => {
  const query = new URLSearchParams({
    page: String(params.page),
    pageSize: String(params.pageSize ?? 25),
  });

  if (params.search?.trim()) query.set("search", params.search.trim());

  return api<Paged<SupplierPaymentSummary>>(`/api/supplier-payments?${query}`);
};

/** One supplier payment in full, with its per-invoice allocations. */
export const getSupplierPayment = (id: number) => api<SupplierPaymentDetail>(`/api/supplier-payments/${id}`);

/** A supplier's open invoices — the picker a payment is allocated over (new and legacy alike). */
export const getOutstandingSupplierInvoices = (supplierId: number) =>
  api<OutstandingSupplierInvoiceLine[]>(`/api/supplier-payments/outstanding?supplierId=${supplierId}`);

/** Record a supplier payment — allocated across open invoices; posts Payment entries and dual-writes the legacy shadow. */
export const createSupplierPayment = (request: CreateSupplierPaymentRequest) =>
  api<SupplierPaymentCreatedResponse>("/api/supplier-payments", { method: "POST", body: request });

/** Void a supplier payment — soft, reason-gated. Reverses each allocation through a compensating entry. A stale row_version is a 409. */
export const voidSupplierPayment = (id: number, expectedRowVersion: number, reason: string) =>
  api<void>(`/api/supplier-payments/${id}?expectedRowVersion=${expectedRowVersion}`, { method: "DELETE", reason });
