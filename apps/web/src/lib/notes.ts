import type { NoteSummary } from "@smartnet/api-client";
import { api } from "./api";

// Generated from the API's OpenAPI schema — see packages/api-client. Re-exported, never redeclared.
export type { NoteSummary } from "@smartnet/api-client";

/** The server's caps, mirrored so the form can say so before the save is spent. */
export const MAX_TITLE_LENGTH = 200;
export const MAX_BODY_LENGTH = 8000;

/**
 * The caller's own notes, most recently touched first.
 *
 * There is no "all notes" call: notes are personal, and the server decides whose they are from the
 * token rather than from anything this module sends.
 */
export const listNotes = () => api<NoteSummary[]>("/api/notes");

export const getNote = (id: number) => api<NoteSummary>(`/api/notes/${id}`);

export const createNote = (title: string, body: string) =>
  api<NoteSummary>("/api/notes", { method: "POST", body: { title, body } });

export const updateNote = (id: number, title: string, body: string, expectedRowVersion: number) =>
  api<NoteSummary>(`/api/notes/${id}`, {
    method: "PUT",
    body: { title, body, expectedRowVersion },
  });

/** Removes a note — soft, so the audit trail survives it. Audited, so a reason is required. */
export const deleteNote = (id: number, expectedRowVersion: number, reason: string) =>
  api<void>(`/api/notes/${id}?expectedRowVersion=${expectedRowVersion}`, {
    method: "DELETE",
    reason,
  });
