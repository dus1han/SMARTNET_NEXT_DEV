import type { LoginResponse, Me } from "@smartnet/api-client";
import { api } from "./api";

// Generated from the API's OpenAPI schema — see packages/api-client.
//
// `Me.permissions` is for rendering only: deciding which links to draw. The server enforces the same
// list on every request, so a client that lies to itself about it gets 403s rather than access.
// Treating it as the control is ISSUES A5, which is the defect this whole phase exists downstream of.
export type { LoginResponse, Me };

export const login = (username: string, password: string) =>
  api<LoginResponse>("/api/auth/login", {
    method: "POST",
    body: { username, password },
  });

export const logout = () => api<void>("/api/auth/logout", { method: "POST" });

/** Who the server says we are. Never trusted from local state — the token can expire mid-session. */
export const me = () => api<Me>("/api/auth/me");

/**
 * Succeeds with 204 and clears the session: the old token still asserts must_change_password, so the
 * user signs in again with the password they just chose rather than carrying a stale token around.
 */
export const changePassword = (currentPassword: string, newPassword: string) =>
  api<void>("/api/auth/change-password", {
    method: "POST",
    body: { currentPassword, newPassword },
  });

/** Mirrors PasswordPolicy.MinimumLength on the server, which is the authority. */
export const MINIMUM_PASSWORD_LENGTH = 10;
