import { api } from "./api";
import type { MailboxListItem, MailFolder, MailHeader, MailMessage, SendMailRequest } from "@smartnet/api-client";

// The signed-in user's own mailboxes and their mail. Everything here is scoped server-side to the caller's
// assigned mailboxes, so none of it takes a user id — the token is the user.

export type { MailboxListItem, MailFolder, MailHeader, MailMessage };

/** The switcher: the user's assigned mailboxes, each with its inbox unread count (or an error). */
export const listMyMailboxes = () => api<MailboxListItem[]>("/api/mail/mailboxes");

/** The folders of one mailbox — Inbox, Sent, Drafts, Trash and any others, each with its unread count. */
export const listFolders = (mailboxId: number) => api<MailFolder[]>(`/api/mail/${mailboxId}/folders`);

const folderQuery = (folder: string) => `folder=${encodeURIComponent(folder)}`;

/** One folder's messages, newest first. */
export const listMessages = (mailboxId: number, folder: string, skip = 0, take = 40) =>
  api<MailHeader[]>(`/api/mail/${mailboxId}/messages?${folderQuery(folder)}&skip=${skip}&take=${take}`);

/** One message in full. Opening it marks it read on the server. */
export const readMessage = (mailboxId: number, folder: string, uid: number) =>
  api<MailMessage>(`/api/mail/${mailboxId}/messages/${uid}?${folderQuery(folder)}`);

/** Mark a message read or unread. */
export const setSeen = (mailboxId: number, folder: string, uid: number, seen: boolean) =>
  api<void>(`/api/mail/${mailboxId}/messages/${uid}/seen?${folderQuery(folder)}&seen=${seen}`, { method: "POST" });

/** Delete a message — to Trash, or for good if it is already in Trash. */
export const deleteMessage = (mailboxId: number, folder: string, uid: number) =>
  api<void>(`/api/mail/${mailboxId}/messages/${uid}?${folderQuery(folder)}`, { method: "DELETE" });

/** Compose, reply or forward — send as this mailbox, and file a copy in Sent. */
export const sendMail = (mailboxId: number, body: SendMailRequest) =>
  api<void>(`/api/mail/${mailboxId}/send`, { method: "POST", body });
