"use client";

/**
 * Autosave for the four create screens.
 *
 * The screen keeps its state exactly as it always has; this hook watches it, and a short pause in
 * typing writes it to the server. The point is narrow: a closed tab, an expired session or a stray
 * reload should not cost somebody a forty-line invoice. Nothing about how a document is *raised*
 * changes — the draft is still posted whole, once, by the create call (D4), and the draft row is
 * deleted after it.
 */

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useSearchParams } from "next/navigation";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { ApiError } from "@/lib/api";
import { createDraftSaver, saveDelayMs, type DraftSaver } from "@/lib/draft-saver";
import {
  createDraft,
  deleteDraft,
  getDraft,
  readPayload,
  updateDraft,
  writePayload,
  type DraftDocType,
} from "@/lib/drafts";

/**
 * How long a pause counts as "stopped typing".
 *
 * Long enough that a burst of typing is one save rather than thirty. The house motion rule — nothing
 * animates while the user is typing — has the same instinct behind it: work with the pauses.
 */
const DEBOUNCE_MS = 1_500;

/**
 * The longest anything may go unsaved, however continuously somebody is typing.
 *
 * <b>A debounce on its own has no ceiling</b>, and that is not a detail. Its timer restarts on every
 * keystroke, so somebody typing steadily through a long fault description never stops for 1.5 seconds
 * and is therefore never saved at all — for as long as they keep going. A power cut or a kernel panic
 * takes the lot, and neither fires `pagehide`, so the flush below cannot help either.
 *
 * With this, the debounce still decides *when* within the window, but the window itself is bounded: a
 * hard crash costs at most five seconds of typing, whatever the user was doing at the time. The cost is
 * one request every five seconds from somebody typing without pause, which is nothing.
 */
const MAX_WAIT_MS = 5_000;

/** What the Drafts list shows about a draft, derived by the screen from its own state. */
export interface DraftSummaryFields {
  /** The customer or supplier, once one is chosen. */
  partyName: string | null;
  /** The running total in major units, or null when nothing priceable is typed. */
  total: number | null;
  lineCount: number;
}

export interface UseDraftAutosaveOptions<T> {
  docType: DraftDocType;
  /** The payload shape's version — see `readPayload`. Bump it when the state's meaning changes. */
  version: number;
  /** The create screen's state, as it should be restored. Must be JSON-serialisable. */
  state: T;
  /**
   * Whether there is anything worth keeping yet.
   *
   * Without this, merely opening New Invoice would leave a draft behind, and the Drafts list would
   * fill with empty rows nobody made — the feature would become noise within a week. A draft starts
   * existing when the user has actually put something in it.
   */
  worthKeeping: boolean;
  summary: DraftSummaryFields;
  /**
   * The draft this screen is resuming — its id and the version it was loaded at.
   *
   * Both or neither. The version is what makes the first autosave an update rather than an insert, and
   * what makes it fail loudly if a colleague has saved the same draft in the meantime.
   */
  resuming?: { id: number; rowVersion: number } | null;
  /** Off while the screen is still loading a draft it is resuming — see the hook's remarks. */
  enabled?: boolean;
}

export type DraftStatus = "idle" | "saving" | "saved" | "error" | "conflict";

export interface DraftAutosave {
  status: DraftStatus;
  /** When the last successful save landed — what the indicator counts from. */
  savedAt: Date | null;
  /** The row backing this screen, once one exists. */
  draftId: number | null;
  /** Why autosave stopped, when it did. */
  error: ApiError | null;
  /**
   * Autosave has stopped for good on this screen — discarded, raised, or beaten by a colleague.
   *
   * The screen must not go on telling somebody their work is being kept once this is true. It briefly
   * did: discarding set the status back to `idle`, and the idle line reads "your work is saved as a
   * draft as you type", which was then a lie — carry on typing, close the tab, lose the lot.
   */
  stopped: boolean;
  /**
   * A colleague has saved this draft, so this screen must not raise the document.
   *
   * <b>Two people holding one draft could otherwise raise it twice</b>, and that is not a lost draft —
   * it is two real quotations, with two numbers, for one customer. Once a save has been refused, this
   * screen is working from a version somebody else has moved past, so it is no longer entitled to turn
   * that version into a document. The screens disable their raise button on it.
   *
   * <b>What this is not.</b> It is a guard on a screen, not a lock on the draft: reloading the page
   * clears it, because the reload fetches the current version and the user is then working from what
   * everyone else has. That is the intended escape hatch, and it is also the limit of this approach —
   * it stops the accident, not somebody determined to have two documents.
   */
  blocked: boolean;
  /** Throw the draft away — the user's decision, from the screen's Discard button. */
  discard: () => Promise<void>;
  /**
   * The document was raised: stop autosaving and clear the draft.
   *
   * Called before the screen navigates away, and deliberately not awaited — the document is already
   * saved, and making the user watch a cleanup finish would be the tail wagging the dog. A delete
   * that fails leaves a stale draft, which the list makes visible and one click removes.
   */
  clear: () => void;
}

/**
 * Autosaves `state` as a draft, and hands back what the screen needs to show for it.
 *
 * <b>`enabled` matters more than it looks.</b> A screen resuming a draft mounts with empty state,
 * fetches the draft, and only then fills the form in. If autosave were running through that, it would
 * see empty state first and — because the draft already exists — overwrite the very row it is about to
 * load. So a resuming screen passes `enabled: false` until the state is in place.
 */
export function useDraftAutosave<T>({
  docType,
  version,
  state,
  worthKeeping,
  summary,
  resuming = null,
  enabled = true,
}: UseDraftAutosaveOptions<T>): DraftAutosave {
  const [draftId, setDraftId] = useState<number | null>(resuming?.id ?? null);
  const [status, setStatus] = useState<DraftStatus>("idle");
  const [savedAt, setSavedAt] = useState<Date | null>(null);
  const [error, setError] = useState<ApiError | null>(null);
  const [stopped, setStopped] = useState(false);

  const queryClient = useQueryClient();

  /**
   * Tells the Drafts lists that what they hold is out of date.
   *
   * <b>Without this a new draft does not appear until a hard refresh.</b> Queries are fresh for 30
   * seconds app-wide (`providers.tsx`), so starting a draft and going straight back to the list served
   * the cached page — the row simply was not there, and the feature looked broken at the first thing
   * anybody tries.
   *
   * Invalidating is nearly free here. The lists are not mounted while somebody is on a create screen,
   * and `invalidateQueries` only refetches *active* queries — so this marks them stale and the refetch
   * happens when the list is next opened, rather than on every keystroke's save.
   */
  const invalidateLists = useCallback(
    () => void queryClient.invalidateQueries({ queryKey: ["drafts", docType] }),
    [queryClient, docType],
  );

  // The ordering — one request at a time, nothing dropped, stop for good on a conflict — lives in
  // `createDraftSaver`, where it is tested without React. This hook is the debounce, the DOM events and
  // the state the screen renders from.
  //
  // `useState` with a lazy initialiser, not a ref: it is built once per mount and never replaced (a new
  // one would forget which row it owns), and unlike a ref it can be read during render without lying
  // about when it changes. The setters it closes over are stable, so the instance never goes stale.
  const [saver] = useState<DraftSaver>(() =>
    createDraftSaver(
      (target, request, keepalive) =>
        target === null
          ? createDraft(request, keepalive)
          : updateDraft(target.id, target.rowVersion, request, keepalive),
      {
        onSaving: () => setStatus("saving"),
        onSaved: (saved) => {
          setDraftId(saved.id);
          setSavedAt(new Date());
          setStatus("saved");
          setError(null);
          invalidateLists();
        },
        onFailed: (failure, conflict) => {
          setError(failure as ApiError);
          setStatus(conflict ? "conflict" : "error");
          // A conflict is terminal: the saver has stopped and will not send again.
          if (conflict) setStopped(true);
        },
      },
    ),
  );

  // A resumed draft arrives from a fetch, so it is null on the first render. Adopting it is what stops
  // the first autosave inserting a second copy of the draft the screen is already showing.
  const adoptedRef = useRef(false);

  useEffect(() => {
    if (adoptedRef.current || resuming == null) return;

    adoptedRef.current = true;
    saver.adopt(resuming.id, resuming.rowVersion);
    setDraftId(resuming.id);
  }, [resuming, saver]);

  const payload = writePayload(version, state);
  // Summary fields are primitives, so they can be depended on directly rather than by identity — an
  // object literal rebuilt every render would restart the debounce on every keystroke.
  const { partyName, total, lineCount } = summary;

  // Whether a draft may exist at all. Once one does, emptying the form is a real edit and is saved as
  // one — otherwise clearing a mistake would leave the mistake on file.
  //
  // Read from `draftId` state rather than the saver, so this is a value React knows changed and the
  // effects below re-run when it does.
  const allowed = worthKeeping || draftId !== null;

  /** When the oldest still-unsaved edit was made — what the ceiling is measured from. */
  const dirtySinceRef = useRef<number | null>(null);

  // The debounce, with a ceiling. The timer restarts on every change, so a pause in typing reaches the
  // server — but never later than MAX_WAIT_MS after the first unsaved keystroke, so somebody typing
  // without pause is still saved. `setPending` is separate from `save` precisely so the flush handlers
  // below can send what is on screen *now* rather than what was on screen when the timer was set.
  useEffect(() => {
    if (!enabled || !allowed) return;

    saver.setPending({ docType, payload, partyName, total, lineCount });

    // First unsaved change since the last successful save starts the clock. `hasUnsaved` is false right
    // after one lands, which is what resets it.
    const now = Date.now();
    dirtySinceRef.current ??= now;

    const delay = saveDelayMs(now - dirtySinceRef.current, {
      debounceMs: DEBOUNCE_MS,
      maxWaitMs: MAX_WAIT_MS,
    });

    const timer = setTimeout(() => {
      void saver.save().then(() => {
        // Only clear the clock once there is genuinely nothing outstanding. A save that failed, or one
        // that raced with further typing, must not look like a fresh start — that would restart the
        // ceiling and let the unsaved window grow without bound again.
        if (!saver.hasUnsaved()) dirtySinceRef.current = null;
      });
    }, delay);

    return () => clearTimeout(timer);
  }, [enabled, allowed, saver, docType, payload, partyName, total, lineCount]);

  // The tab is going away. `keepalive` is what makes this worth doing: an ordinary fetch started here is
  // cancelled with the page, which is exactly the save that matters most.
  //
  // `visibilitychange` rather than `beforeunload`: on mobile a tab is often discarded without ever firing
  // an unload, and hidden is the last moment the page is reliably told anything.
  useEffect(() => {
    if (!enabled) return;

    function flush() {
      if (document.visibilityState !== "hidden") return;
      if (!allowed) return;

      // The saver decides whether there is anything to send, so switching tabs back and forth on an
      // unchanged draft costs nothing.
      void saver.save(true);
    }

    document.addEventListener("visibilitychange", flush);
    window.addEventListener("pagehide", flush);

    return () => {
      document.removeEventListener("visibilitychange", flush);
      window.removeEventListener("pagehide", flush);
    };
  }, [enabled, allowed, saver]);

  // Leaving the screen inside the app — a pending edit would otherwise die with the debounce timer.
  //
  // Only ever an update, never a create: React remounts components in development's strict mode, and a
  // create here would leave a duplicate draft behind every time. If no row exists yet, the most that is
  // lost is the second and a half since the user last paused.
  useEffect(() => () => {
    if (saver.snapshot().id === null) return;

    void saver.save(true);
  }, [saver]);

  const discard = useCallback(async () => {
    // Stopped before the delete, not after: an autosave landing in between would recreate the row that
    // is about to be removed, and the user would find the draft they just discarded back in the list.
    const id = saver.stop();

    setStopped(true);
    setDraftId(null);
    setStatus("idle");
    setSavedAt(null);

    // Deliberately not caught. The screen awaits this and keeps its dialog open on a failure, because
    // "discarded" is not something to report when the row is still there.
    if (id !== null) await deleteDraft(id);

    invalidateLists();
  }, [saver, invalidateLists]);

  const clear = useCallback(() => {
    const id = saver.stop();
    setStopped(true);

    // Not awaited: the document is raised and the screen is navigating. See the interface's remarks.
    if (id !== null) void deleteDraft(id).catch(() => {});

    // So the Drafts tab does not still show a draft that has just become a document.
    invalidateLists();
  }, [saver, invalidateLists]);

  return {
    status,
    savedAt,
    draftId,
    error,
    stopped,
    blocked: status === "conflict",
    discard,
    clear,
  };
}

export interface DraftResume {
  /** Pass straight to `useDraftAutosave`, so its first save updates this row rather than adding one. */
  resuming: { id: number; rowVersion: number } | null;
  /** True while the draft is being fetched. Autosave must stay off until it is false — see below. */
  loading: boolean;
  /** The draft exists but was written by a version of this screen that no longer understands it. */
  unreadable: boolean;
  /** Whatever went wrong fetching it — most often a draft somebody else has already discarded. */
  error: ApiError | null;
}

/**
 * Loads the draft a create screen was opened to resume (`?draft=123`) and hands its state to `apply`.
 *
 * <b>Why the screen applies the state rather than this hook setting it.</b> Each create screen holds its
 * own fields in its own `useState` calls, and only it knows which is which; a hook that tried to restore
 * them would need to know every screen's shape. So this owns the fetch, the decode and the one-shot
 * guard — the parts that are identical and easy to get subtly wrong — and the screen owns the assignment.
 *
 * <b>`loading` is not cosmetic.</b> A resuming screen mounts with empty fields and fills them in after
 * the fetch. Autosave running through that window would save the empty state over the draft it is in the
 * middle of loading — so it is passed as `enabled: !loading`, and the emptiness is never written back.
 */
export function useDraftResume<T>(version: number, apply: (state: T) => void): DraftResume {
  // `useSearchParams`, not `window.location.search`.
  //
  // This read used to come from `location`, copying the job-card screen's `?print=1` trick, to keep the
  // page out of a Suspense boundary. It silently did not work: arriving here by a client-side navigation
  // from the Drafts tab, the component mounts before the browser URL has been rewritten, so the read
  // returned null, no draft was ever fetched, and the form sat there empty with no error to explain it.
  // The API log settled it — a POST and a PUT for the draft, and never a GET.
  //
  // This hook reads the router's own state instead, which is correct whichever way the screen was
  // reached. The cost is that every screen using it needs a Suspense boundary, which the four create
  // pages now have; that is the documented trade and it is cheaper than a resume that quietly fails.
  const searchParams = useSearchParams();
  const raw = searchParams.get("draft");
  const parsed = raw === null ? Number.NaN : Number(raw);
  const id = Number.isInteger(parsed) && parsed > 0 ? parsed : null;

  const draft = useQuery({
    queryKey: ["draft", id],
    queryFn: () => getDraft(id!),
    enabled: id !== null,
    // A draft is resumed once, at mount. Refetching it — on a window focus, say — would hand back a
    // payload the user has since typed past.
    staleTime: Infinity,
    refetchOnWindowFocus: false,
    retry: false,
  });

  // Everything below is *derived* from the fetch rather than copied into state by an effect. The copy is
  // what invites the bug: two sources for one fact, and a render in between where they disagree — which,
  // for `loading`, is a render where autosave is on and the form is still empty.
  const loaded = draft.data ?? null;

  const state = useMemo(
    () => (loaded === null ? null : readPayload<T>(loaded.payload, version)),
    [loaded, version],
  );

  const resuming = useMemo(
    () => (loaded === null || state === null ? null : { id: loaded.id, rowVersion: loaded.rowVersion }),
    [loaded, state],
  );

  // The screen passes an inline closure, so its identity changes on every render. Kept in a ref, and
  // synced in its own effect declared *before* the one that calls it — effects run in declaration order,
  // so the version applied below is always the current one.
  const applyRef = useRef(apply);

  useEffect(() => {
    applyRef.current = apply;
  });

  const appliedRef = useRef(false);

  useEffect(() => {
    if (appliedRef.current || state === null) return;

    // Once only. Re-applying would overwrite whatever the user has typed since the draft loaded.
    appliedRef.current = true;
    applyRef.current(state);
  }, [state]);

  return {
    resuming,
    // False once the fetch has settled either way. A draft that failed to load must not leave autosave
    // off forever, or the screen silently stops keeping the work being done on it right now.
    loading: id !== null && !draft.isFetched,
    unreadable: loaded !== null && state === null,
    error: (draft.error as ApiError | null) ?? null,
  };
}
