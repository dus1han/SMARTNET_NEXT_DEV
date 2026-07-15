import type {
  CreateUserResponse,
  PermissionCatalogueEntry,
  ResetPasswordResponse,
  RoleSummary,
  UserSummary,
} from "@smartnet/api-client";
import { api } from "./api";

// Generated from the API's OpenAPI schema — see packages/api-client. Re-exported, never redeclared.
export type { PermissionCatalogueEntry, RoleSummary, UserSummary };

export const listUsers = () => api<UserSummary[]>("/api/users");

export const listRoles = () => api<RoleSummary[]>("/api/roles");

/** Every permission that exists, so the editor is a list of real toggles, not magic strings. */
export const listPermissions = () =>
  api<PermissionCatalogueEntry[]>("/api/roles/permissions");

/**
 * Sets a user's permissions directly — the whole set, in one request.
 *
 * `reason` is mandatory: changing what someone may do is one of the audited actions. The server
 * makes the user's effective permissions equal exactly this list, so the checkboxes are the truth.
 */
export const setUserPermissions = (id: number, permissions: string[], reason: string) =>
  api<void>(`/api/users/${id}/permissions`, {
    method: "PUT",
    body: { permissions },
    reason,
  });

/**
 * The temporary password comes back exactly once and is never retrievable again — it is stored only
 * as an Argon2id hash. Show it to the administrator, or it is lost and they must reset again.
 */
export const createUser = (username: string, name: string, roleIds: number[]) =>
  api<CreateUserResponse>("/api/users", {
    method: "POST",
    body: { username, name, roleIds },
  });

/** `reason` is mandatory: the server rejects a permission change that does not explain itself. */
export const updateUser = (id: number, name: string, roleIds: number[], reason: string) =>
  api<void>(`/api/users/${id}`, {
    method: "PUT",
    body: { name, roleIds },
    reason,
  });

export const resetPassword = (id: number, reason: string) =>
  api<ResetPasswordResponse>(`/api/users/${id}/reset-password`, {
    method: "POST",
    reason,
  });

export const disableUser = (id: number, reason: string) =>
  api<void>(`/api/users/${id}`, { method: "DELETE", reason });

/** Mirrors AUDIT.md §5: a reason under this length is not a reason. */
export const MINIMUM_REASON_LENGTH = 10;
