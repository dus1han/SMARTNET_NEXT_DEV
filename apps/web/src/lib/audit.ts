import type { AuditActor, AuditEntry, AuditFacets, AuditLogResponse } from "@smartnet/api-client";
import { api } from "./api";

// Generated from the API's OpenAPI schema — see packages/api-client. Re-exported, never redeclared.
export type { AuditActor, AuditEntry, AuditFacets, AuditLogResponse };

/**
 * The filters the audit viewer narrows the log by — every one optional, every one on the request.
 *
 * There is deliberately no company filter: the log is scoped server-side to the companies the token
 * grants, and it holds rows that belong to no company at all (a login happens before there is an
 * active company to attribute it to). A company selector would both duplicate the server's scoping
 * and imply those company-less rows can be filtered away, which they cannot.
 */
export interface AuditFilter {
  /** ISO `yyyy-MM-dd`. Inclusive; an omitted end is unbounded on that side. */
  from?: string;
  to?: string;
  /** A specific actor's id, or every actor when undefined. */
  user?: number;
  /** One AuditAction — "Create", "Login", "Export", … — or every action when undefined. */
  action?: string;
  /** The CLR entity name as stored — "User", "Customer", "Report". */
  entityType?: string;
}

function auditQuery(filter: AuditFilter): string {
  const params = new URLSearchParams();

  if (filter.from) params.set("from", filter.from);
  if (filter.to) params.set("to", filter.to);
  if (filter.user !== undefined) params.set("user", String(filter.user));
  if (filter.action) params.set("action", filter.action);
  if (filter.entityType) params.set("entityType", filter.entityType);

  const query = params.toString();
  return query ? `?${query}` : "";
}

/** A page of the whole audit log, filtered and newest first. Capped, with the true total alongside. */
export const getAuditLog = (filter: AuditFilter) =>
  api<AuditLogResponse>(`/api/history/log${auditQuery(filter)}`);

/** The entity types and users present in the visible log — the filter dropdowns' options. */
export const getAuditFacets = () => api<AuditFacets>("/api/history/log/facets");

/**
 * The audit actions, in a sensible reading order. Fixed by the API's `AuditAction` enum, so it is a
 * constant here rather than a facet query — every possible action is a valid filter whether or not
 * the log happens to hold one yet.
 */
export const AUDIT_ACTIONS = [
  "Create",
  "Update",
  "Delete",
  "Restore",
  "Login",
  "Print",
  "Email",
  "Export",
] as const;
