import type {
  CustomerReceiptCreatedResponse,
  ReinstateSuspensionRequest,
  SettleSuspensionRequest,
  SuspendedReceivableSummary,
  SuspendReceivableRequest,
  SuspensionCreatedResponse,
} from "@smartnet/api-client";
import { api } from "./api";

// Generated from the API's OpenAPI schema — see packages/api-client. Re-exported, never redeclared.
export type {
  InvoiceSuspension,
  SettleSuspensionRequest,
  SuspendedReceivableSummary,
  SuspendReceivableRequest,
} from "@smartnet/api-client";

/** The suspended account: what is on it now, or (active = false) what has come back off it. */
export const getSuspendedReceivables = (active: boolean) =>
  api<SuspendedReceivableSummary[]>(`/api/suspended-receivables?active=${active}`);

/** Move part or all of an invoice's outstanding to the suspended account. */
export const suspendReceivable = (request: SuspendReceivableRequest) =>
  api<SuspensionCreatedResponse>("/api/suspended-receivables", { method: "POST", body: request });

/** Bring the whole suspended amount back onto its invoice. */
export const reinstateSuspension = (id: number, request: ReinstateSuspensionRequest) =>
  api<void>(`/api/suspended-receivables/${id}/reinstate`, { method: "POST", body: request });

/** Bring the suspended amount back and record the payment against it, in one act. */
export const settleSuspension = (id: number, request: SettleSuspensionRequest) =>
  api<CustomerReceiptCreatedResponse>(`/api/suspended-receivables/${id}/settle`, { method: "POST", body: request });
