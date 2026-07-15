import type {
  CreateSupplierResponse,
  SaveSupplierRequest,
  SupplierSummary,
} from "@smartnet/api-client";
import { api } from "./api";

// Generated from the API's OpenAPI schema — see packages/api-client. Re-exported, never redeclared.
export type { SaveSupplierRequest, SupplierSummary };

export const listSuppliers = () => api<SupplierSummary[]>("/api/suppliers");

/**
 * Creates a supplier. The code — "S-87" — is allocated by the server from the same sequence the
 * legacy app uses, and comes back in the response; the client never invents it.
 */
export const createSupplier = (supplier: SaveSupplierRequest) =>
  api<CreateSupplierResponse>("/api/suppliers", { method: "POST", body: supplier });

export const updateSupplier = (id: number, supplier: SaveSupplierRequest) =>
  api<void>(`/api/suppliers/${id}`, { method: "PUT", body: supplier });

/** `reason` is mandatory. The server rejects a delete that does not explain itself. */
export const deleteSupplier = (id: number, reason: string) =>
  api<void>(`/api/suppliers/${id}`, { method: "DELETE", reason });
