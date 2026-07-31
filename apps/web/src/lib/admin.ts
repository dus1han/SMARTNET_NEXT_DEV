import type {
  CreateUserResponse,
  MailboxSummary,
  PermissionCatalogueEntry,
  ResetPasswordResponse,
  RoleSummary,
  UserSummary,
} from "@smartnet/api-client";
import { api } from "./api";

// Generated from the API's OpenAPI schema — see packages/api-client. Re-exported, never redeclared.
export type { MailboxSummary, PermissionCatalogueEntry, RoleSummary, UserSummary };

export const listUsers = () => api<UserSummary[]>("/api/users");

/** The mailboxes an administrator may assign — served under `users`, so no `mail_accounts` needed. */
export const listAssignableMailboxes = () => api<MailboxSummary[]>("/api/users/mailboxes");

/** Sets a user's whole mailbox set to exactly these ids. */
export const setUserMailboxes = (id: number, mailAccountIds: number[]) =>
  api<void>(`/api/users/${id}/mailboxes`, { method: "PUT", body: { mailAccountIds } });

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

/**
 * Renames a user — their full name, and their username while that is still allowed.
 *
 * `roleIds` is not a detail to be defaulted: `PUT /api/users/{id}` sets the user's whole role set to
 * whatever it is given, so a rename must send back the roles they already hold or it strips them on
 * the way past. Callers pass `user.roles.map((r) => r.id)`.
 *
 * `username` is omitted to leave it alone. The server accepts one only while the account has raised
 * nothing — `user.hasTransactions` says whether to offer the field, and the server decides for real.
 *
 * `expectedRowVersion` is the version the editor opened on. Stale is a 409 for the same reason as
 * `setUserPermissions`: this request can move roles, so overwriting somebody else's save is a
 * privilege question and not merely a lost edit.
 */
export const updateUser = (
  id: number,
  values: { name: string; username?: string; roleIds: number[] },
  reason: string,
  expectedRowVersion: number,
) =>
  api<void>(`/api/users/${id}`, {
    method: "PUT",
    body: {
      name: values.name,
      roleIds: values.roleIds,
      username: values.username,
      expectedRowVersion,
    },
    reason,
  });

export const resetPassword = (id: number, reason: string) =>
  api<ResetPasswordResponse>(`/api/users/${id}/reset-password`, {
    method: "POST",
    reason,
  });

export const disableUser = (id: number, reason: string) =>
  api<void>(`/api/users/${id}`, { method: "DELETE", reason });

/** The inverse of {@link disableUser}: they can sign in again, with the password they had. */
export const enableUser = (id: number, reason: string) =>
  api<void>(`/api/users/${id}/enable`, { method: "POST", reason });

/**
 * Removes the account outright, rather than disabling it — the server allows this only while the
 * account has raised nothing (`user.hasTransactions` is false), because after that the documents it
 * raised are attributed to it. Not undoable: {@link disableUser} is the reversible one.
 */
export const deleteUserPermanently = (id: number, reason: string) =>
  api<void>(`/api/users/${id}/permanent`, { method: "DELETE", reason });

/** Mirrors AUDIT.md §5: a reason under this length is not a reason. */
export const MINIMUM_REASON_LENGTH = 10;
