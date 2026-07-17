import type {
  CloseJobCardRequest,
  CreateJobCardRequest,
  JobCardCreatedResponse,
  JobCardDetail,
  JobCardSummary,
} from "@smartnet/api-client";
import { api } from "./api";

// Generated from the API's OpenAPI schema — see packages/api-client. Re-exported, never redeclared.
export type {
  CloseJobCardRequest,
  CreateJobCardRequest,
  JobCardCreatedResponse,
  JobCardDetail,
  JobCardLineDetail,
  JobCardSummary,
} from "@smartnet/api-client";

/** The job cards this app has raised and the legacy ones adopted, newest first. */
export const getJobCards = () => api<JobCardSummary[]>("/api/job-cards");

/** One job card in full, with its serial-tracked lines and (once closed) cost/sell. */
export const getJobCard = (id: number) => api<JobCardDetail>(`/api/job-cards/${id}`);

/** Book in a job card — PENDING, with structured serial lines. No tax, no ledger, no stock. */
export const createJobCard = (request: CreateJobCardRequest) =>
  api<JobCardCreatedResponse>("/api/job-cards", { method: "POST", body: request });

/** Close a job — the guarded PENDING → CLOSED transition (closing means completed). A stale row_version is a 409. */
export const closeJobCard = (id: number, request: CloseJobCardRequest) =>
  api<void>(`/api/job-cards/${id}/close`, { method: "POST", body: request });
