import type {
  CreatePurchaseOrderRequest,
  EmailDocumentRequest,
  EmailDocumentResponse,
  InvoiceTaxRate,
  PurchaseOrderCreatedResponse,
  PurchaseOrderDetail,
  EditPurchaseOrderRequest,
  PurchaseOrderDeleted,
  PurchaseOrderEditedResponse,
  PurchaseOrderRecipients,
  PurchaseOrderSummary,
} from "@smartnet/api-client";
import { api } from "./api";

// Generated from the API's OpenAPI schema — see packages/api-client. Re-exported, never redeclared.
export type {
  CreatePurchaseOrderRequest,
  EmailDocumentRequest,
  EmailDocumentResponse,
  PurchaseOrderCreatedResponse,
  PurchaseOrderDetail,
  EditPurchaseOrderRequest,
  PurchaseOrderDeleted,
  PurchaseOrderEditedResponse,
  PurchaseOrderRecipients,
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

/**
 * Who this order can be emailed to — the supplier's addresses on file.
 *
 * Suppliers have no contacts table, so the addresses come from the supplier's own email column, split
 * where it holds more than one. `blocked` says why a send would fail before any are chosen.
 */
export const purchaseOrderRecipients = (id: number) =>
  api<PurchaseOrderRecipients>(`/api/purchase-orders/${id}/recipients`);

/**
 * Emails the purchase order as a PDF attachment to the chosen supplier addresses.
 *
 * Resolves 200 even when the mail server refused it — the response carries `sent` and the reason.
 */
export const emailPurchaseOrder = (id: number, request: EmailDocumentRequest) =>
  api<EmailDocumentResponse>(`/api/purchase-orders/${id}/email`, { method: "POST", body: request });

/**
 * Edit a purchase order — versioned, reason-gated. A stale row_version is a 409.
 *
 * An order posts no ledger entry and no stock movement, so an edit re-values the document alone.
 * Moving the date re-rates it at the VAT rate in force then.
 */
export const editPurchaseOrder = (id: number, request: EditPurchaseOrderRequest, reason: string) =>
  api<PurchaseOrderEditedResponse>(`/api/purchase-orders/${id}`, { method: "PUT", body: request, reason });

/** Void a purchase order — soft, recoverable, reason-gated. Nothing to reverse; an order posted nothing. */
export const deletePurchaseOrder = (id: number, expectedRowVersion: number, reason: string) =>
  api<PurchaseOrderDeleted>(`/api/purchase-orders/${id}?expectedRowVersion=${expectedRowVersion}`, {
    method: "DELETE",
    reason,
  });
