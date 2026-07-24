import { api } from "./api";
import type {
  MailAccount,
  SaveMailAccountRequest,
  MailServerSettings,
  SaveMailServerSettingsRequest,
  MailDomain,
} from "@smartnet/api-client";

// The mail server and accounts are global (not company-scoped), so none of these pass a companyId.

// --- The shared server ---------------------------------------------------------------------------

export const getMailServerSettings = () =>
  api<MailServerSettings>("/api/mail-accounts/server-settings");

export const saveMailServerSettings = (body: SaveMailServerSettingsRequest, reason: string) =>
  api<void>("/api/mail-accounts/server-settings", { method: "PUT", body, reason });

// --- Mailboxes -----------------------------------------------------------------------------------

/** The fixed domain for new addresses — readable without the cPanel-screen permission. */
export const getMailDomain = () => api<MailDomain>("/api/mail-accounts/domain");

export const listMailAccounts = () => api<MailAccount[]>("/api/mail-accounts");

export const createMailAccount = (body: SaveMailAccountRequest, reason: string) =>
  api<MailAccount>("/api/mail-accounts", { method: "POST", body, reason });

/** `password: null` in the body leaves the stored one alone — send that when it wasn't retyped. */
export const updateMailAccount = (id: number, body: SaveMailAccountRequest, reason: string) =>
  api<void>(`/api/mail-accounts/${id}`, { method: "PUT", body, reason });

export const deleteMailAccount = (id: number, reason: string) =>
  api<void>(`/api/mail-accounts/${id}`, { method: "DELETE", reason });

export const testMailAccount = (id: number, to: string) =>
  api<void>(`/api/mail-accounts/${id}/test`, { method: "POST", body: { to } });
