import { api } from "./api";
import type { MailboxListItem, MailHeader, MailMessage, SendMailRequest } from "@smartnet/api-client";

// The signed-in user's own mailboxes and their mail. Everything here is scoped server-side to the caller's
// assigned mailboxes, so none of it takes a user id — the token is the user.

export type { MailboxListItem, MailHeader, MailMessage };

/** The switcher: the user's assigned mailboxes, each with its unread count (or an error). */
export const listMyMailboxes = () => api<MailboxListItem[]>("/api/mail/mailboxes");

/** One mailbox's inbox, newest first. */
export const listInbox = (mailboxId: number, skip = 0, take = 40) =>
  api<MailHeader[]>(`/api/mail/${mailboxId}/messages?skip=${skip}&take=${take}`);

/** One message in full. Opening it marks it read on the server. */
export const readMessage = (mailboxId: number, uid: number) =>
  api<MailMessage>(`/api/mail/${mailboxId}/messages/${uid}`);

/** Compose or reply — send as this mailbox. */
export const sendMail = (mailboxId: number, body: SendMailRequest) =>
  api<void>(`/api/mail/${mailboxId}/send`, { method: "POST", body });
