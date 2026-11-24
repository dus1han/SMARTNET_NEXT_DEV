import type {
  CreateItemResponse,
  CreateStockAdjustmentRequest,
  ItemLastPrice,
  ItemStock,
  ItemSummary,
  SaveItemRequest,
} from "@smartnet/api-client";
import { api } from "./api";

// Generated from the API's OpenAPI schema — see packages/api-client. Re-exported, never redeclared.
export type { ItemLastPrice, ItemStock, ItemSummary, SaveItemRequest };

export const listItems = () => api<ItemSummary[]>("/api/items");

/**
 * What this customer last paid for each item they have bought — invoices only, in the given company. An
 * item they never bought is absent. `excludeInvoiceId` leaves out the invoice being edited.
 */
export const getLastPrices = (customerId: number, companyId: number | null, excludeInvoiceId?: number) => {
  const query = new URLSearchParams({ customerId: String(customerId) });
  if (companyId != null) query.set("companyId", String(companyId));
  if (excludeInvoiceId != null) query.set("excludeInvoiceId", String(excludeInvoiceId));
  return api<ItemLastPrice[]>(`/api/items/last-prices?${query}`);
};

/**
 * Creates an item. The code — "I-501" — is allocated by the server from the same sequence the legacy
 * app uses. Callable from the items list AND from inside the invoice screen (Phase 5).
 */
export const createItem = (item: SaveItemRequest) =>
  api<CreateItemResponse>("/api/items", { method: "POST", body: item });

export const updateItem = (id: number, item: SaveItemRequest) =>
  api<void>(`/api/items/${id}`, { method: "PUT", body: item });

/** `reason` is mandatory. The server refuses an item that still has a stock ledger. */
export const deleteItem = (id: number, reason: string) =>
  api<void>(`/api/items/${id}`, { method: "DELETE", reason });

// --- Stock -----------------------------------------------------------------------------------

/** An item's balance, its movement ledger, and the legacy receipt batches. */
export const getItemStock = (id: number) => api<ItemStock>(`/api/items/${id}/stock`);

/** Records a stock adjustment — a new ledger movement, never an edit of a balance. */
export const adjustStock = (id: number, adjustment: CreateStockAdjustmentRequest) =>
  api<void>(`/api/items/${id}/stock/adjustments`, { method: "POST", body: adjustment });
