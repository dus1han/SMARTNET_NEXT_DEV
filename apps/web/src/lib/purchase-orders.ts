import type {
  CreatePurchaseOrderRequest,
  InvoiceTaxRate,
  PurchaseOrderCreatedResponse,
  PurchaseOrderDetail,
  PurchaseOrderSummary,
} from "@smartnet/api-client";
import { api } from "./api";

// Generated from the API's OpenAPI schema — see packages/api-client. Re-exported, never redeclared.
export type {
  CreatePurchaseOrderRequest,
  PurchaseOrderCreatedResponse,
  PurchaseOrderDetail,
  PurchaseOrderSummary,
} from "@smartnet/api-client";

/** The purchase orders this app has raised and the legacy ones adopted from the old system, newest first. */
export const getPurchaseOrders = () => api<PurchaseOrderSummary[]>("/api/purchase-orders");

/** One purchase order in full, with its lines. */
export const getPurchaseOrder = (id: number) => api<PurchaseOrderDetail>(`/api/purchase-orders/${id}`);

/**
 * The one VAT rate a PO raised for this company on this date will carry — the same server engine the save
 * uses, gated by the purchase-order permission. Fetched once when company or date changes.
 */
export const getPurchaseOrderTaxRate = (companyId: number, date: string) =>
  api<InvoiceTaxRate>(`/api/purchase-orders/tax-rate?companyId=${companyId}&date=${date}`);

/** Raise a purchase order — the whole document, posted once. No ledger, no stock (an order, not a receipt). */
export const createPurchaseOrder = (request: CreatePurchaseOrderRequest) =>
  api<PurchaseOrderCreatedResponse>("/api/purchase-orders", { method: "POST", body: request });
