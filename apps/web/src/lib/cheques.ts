import type {
  CreateChequeRequest,
  ChequeCreatedResponse,
  ChequeDetail,
  ChequeSummary,
} from "@smartnet/api-client";
import { api } from "./api";

// Generated from the API's OpenAPI schema — see packages/api-client. Re-exported, never redeclared.
export type { CreateChequeRequest, ChequeCreatedResponse, ChequeDetail, ChequeSummary } from "@smartnet/api-client";

/** The cheques this app has recorded and the legacy ones adopted, newest first. */
export const getCheques = () => api<ChequeSummary[]>("/api/cheques");

/** One cheque in full — this app's own or a legacy one. */
export const getCheque = (id: number) => api<ChequeDetail>(`/api/cheques/${id}`);

/** Record a cheque — a standalone written record; dual-writes the legacy row for the ChequeReport. */
export const createCheque = (request: CreateChequeRequest) =>
  api<ChequeCreatedResponse>("/api/cheques", { method: "POST", body: request });

/** Void a cheque — soft, reason-gated. A stale row_version is a 409. */
export const voidCheque = (id: number, expectedRowVersion: number, reason: string) =>
  api<void>(`/api/cheques/${id}?expectedRowVersion=${expectedRowVersion}`, { method: "DELETE", reason });
