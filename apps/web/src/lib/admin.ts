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
/**
 * Replaces a user's whole permission set.
 *
 * `expectedRowVersion` is the user's version when the editor was opened. A stale one is a 409: this
 * replaces the *whole* set, so applying it over somebody else's change does not lose an edit — it
 * silently reinstates a permission another administrator has just revoked.
 */
export const setUserPermissions = (
  id: number,
  permissions: string[],
  reason: string,
  expectedRowVersion: number,
) =>
  api<void>(`/api/users/${id}/permissions`, {
    method: "PUT",
    body: { permissions, expectedRowVersion },
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

// No `updateUser` here on purpose. `PUT /api/users/{id}` (name + roles) exists and is protected, but
// nothing in this app calls it: the users screen assigns access through `setUserPermissions` below,
// which is permission assignment without the ceremony of roles. A client wrapper with no call site is
// a thing that rots — it was still passing the old argument list long after the endpoint changed.

export const resetPassword = (id: number, reason: string) =>
  api<ResetPasswordResponse>(`/api/users/${id}/reset-password`, {
    method: "POST",
    reason,
  });

export const disableUser = (id: number, reason: string) =>
  api<void>(`/api/users/${id}`, { method: "DELETE", reason });

/** Mirrors AUDIT.md §5: a reason under this length is not a reason. */
export const MINIMUM_REASON_LENGTH = 10;
