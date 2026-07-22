/**
 * The sequencing behind draft autosave, with no React in it.
 *
 * Kept separate from `useDraftAutosave` because this is the part that is actually hard: one request at
 * a time, never lose an edit that arrives while one is in flight, stop for good on a conflict, and know
 * whether the next call is an insert or an update. A bug in any of those loses somebody's work quietly —
 * the indicator says "saved" and it is not — so it is written where it can be tested directly rather
 * than through a rendered component.
 *
 * The hook owns the debounce, the DOM events and the React state. This owns the ordering.
 */

import type { DraftSaved, SaveDraftRequest } from "./drafts";

/** What actually talks to the server. Injected so the ordering can be tested without one. */
export type SendDraft = (
  /** The row to update, or null to create one. */
  target: { id: number; rowVersion: number } | null,
  request: SaveDraftRequest,
  keepalive: boolean,
) => Promise<DraftSaved>;

export interface DraftSaverEvents {
  onSaving: () => void;
  onSaved: (saved: DraftSaved) => void;
  /** `conflict` is terminal — the saver has stopped and will not send again. */
  onFailed: (error: unknown, conflict: boolean) => void;
}

export interface DraftSaverSnapshot {
  id: number | null;
  rowVersion: number;
  stopped: boolean;
}

/**
 * Creates a saver.
 *
 * `pending` is set by the caller whenever the form changes; `save()` sends whatever is pending at the
 * moment it runs — not what was pending when it was scheduled. That distinction is the whole point: a
 * save scheduled two seconds ago must not overwrite the server with two-second-old state.
 */
export function createDraftSaver(send: SendDraft, events: DraftSaverEvents) {
  let id: number | null = null;
  let rowVersion = 0;
  let lastSaved: string | null = null;
  let pending: SaveDraftRequest | null = null;
  let inFlight = false;
  let queued = false;
  let stopped = false;

  async function save(keepalive = false): Promise<void> {
    if (stopped) return;

    // Already sending. Remember that something changed and send again when this one lands, rather than
    // dropping it — dropping it is a silent loss of exactly the edits typed during a slow request.
    if (inFlight) {
      queued = true;
      return;
    }

    const request = pending;

    // Nothing to say. Compared against what was last stored, so repeated flushes (a tab switched back
    // and forth, say) cost nothing.
    if (request === null || request.payload === lastSaved) return;

    inFlight = true;
    events.onSaving();

    try {
      const saved = await send(id === null ? null : { id, rowVersion }, request, keepalive);

      id = saved.id;
      rowVersion = saved.rowVersion;
      lastSaved = request.payload;

      events.onSaved(saved);
    } catch (error) {
      // A conflict is terminal. Somebody else has this draft open and saved first; carrying on would
      // overwrite their work at the next successful attempt, and the one unacceptable outcome is for
      // both of them to believe they are being saved.
      const conflict = (error as { status?: number } | null)?.status === 409;
      if (conflict) stopped = true;

      events.onFailed(error, conflict);
    } finally {
      inFlight = false;

      if (queued) {
        queued = false;
        // Recurses at most as often as the form changes, and the `payload === lastSaved` guard above
        // ends the chain as soon as the server has caught up with the screen.
        if (!stopped) await save(keepalive);
      }
    }
  }

  return {
    save,

    /** The latest state of the form. Cheap — called on every change, sends nothing. */
    setPending(request: SaveDraftRequest) {
      pending = request;
    },

    /** Take over a draft loaded from the server, so the next save updates it rather than adding one. */
    adopt(draftId: number, version: number) {
      id = draftId;
      rowVersion = version;
    },

    /** The document was raised, or the draft was discarded. Nothing is sent after this. */
    stop() {
      stopped = true;
      pending = null;
      const discarded = id;
      id = null;
      return discarded;
    },

    /** True when there is an edit the server has not got. What "unsaved" means, for a flush decision. */
    hasUnsaved(): boolean {
      return pending !== null && pending.payload !== lastSaved;
    },

    snapshot(): DraftSaverSnapshot {
      return { id, rowVersion, stopped };
    },
  };
}

export type DraftSaver = ReturnType<typeof createDraftSaver>;
