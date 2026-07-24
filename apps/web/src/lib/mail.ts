import { api, API_BASE_URL } from "./api";
import type { MailboxListItem, MailFolder, MailHeader, MailMessage, MailContactSuggestion } from "@smartnet/api-client";

// The signed-in user's own mailboxes and their mail. Everything here is scoped server-side to the caller's
// assigned mailboxes, so none of it takes a user id — the token is the user.

export type { MailboxListItem, MailFolder, MailHeader, MailMessage, MailContactSuggestion };

/** The switcher: the user's assigned mailboxes, each with its inbox unread count (or an error). */
export const listMyMailboxes = () => api<MailboxListItem[]>("/api/mail/mailboxes");

/** Recipient suggestions (customer contacts) for the To/Cc/Bcc autocomplete. */
export const listMailContacts = () => api<MailContactSuggestion[]>("/api/mail/contacts");

/** The folders of one mailbox — Inbox, Sent, Drafts, Trash and any others, each with its unread count. */
export const listFolders = (mailboxId: number) => api<MailFolder[]>(`/api/mail/${mailboxId}/folders`);

const folderQuery = (folder: string) => `folder=${encodeURIComponent(folder)}`;

/** One folder's messages, newest first — or those matching `search` (subject/from/to/body) when given. */
export const listMessages = (mailboxId: number, folder: string, take = 40, search?: string) => {
  const q = search ? `&search=${encodeURIComponent(search)}` : "";
  return api<MailHeader[]>(`/api/mail/${mailboxId}/messages?${folderQuery(folder)}&skip=0&take=${take}${q}`);
};

/** One message in full. Opening it marks it read on the server. */
export const readMessage = (mailboxId: number, folder: string, uid: number) =>
  api<MailMessage>(`/api/mail/${mailboxId}/messages/${uid}?${folderQuery(folder)}`);

/** The direct download URL of one attachment — used as a link href, so the browser downloads it. */
export const attachmentUrl = (mailboxId: number, folder: string, uid: number, index: number) =>
  `${API_BASE_URL}/api/mail/${mailboxId}/messages/${uid}/attachments/${index}?${folderQuery(folder)}`;

/** Mark a message read or unread. */
export const setSeen = (mailboxId: number, folder: string, uid: number, seen: boolean) =>
  api<void>(`/api/mail/${mailboxId}/messages/${uid}/seen?${folderQuery(folder)}&seen=${seen}`, { method: "POST" });

/** Delete a message — to Trash, or for good if it is already in Trash. */
export const deleteMessage = (mailboxId: number, folder: string, uid: number) =>
  api<void>(`/api/mail/${mailboxId}/messages/${uid}?${folderQuery(folder)}`, { method: "DELETE" });

export interface OutgoingMail {
  to: string;
  cc: string;
  bcc: string;
  subject: string;
  body: string;
  files: File[];
}

/** Compose, reply or forward — send as this mailbox (multipart, so files ride along), and copy to Sent. */
export const sendMail = (mailboxId: number, mail: OutgoingMail) => {
  const form = new FormData();
  form.append("to", mail.to);
  form.append("cc", mail.cc);
  form.append("bcc", mail.bcc);
  form.append("subject", mail.subject);
  form.append("body", mail.body);
  for (const file of mail.files) form.append("files", file);
  return api<void>(`/api/mail/${mailboxId}/send`, { method: "POST", body: form });
};
