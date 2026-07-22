"use client";

/**
 * What a create screen says about its own draft.
 *
 * Quiet by design. Autosave that announces itself every two seconds is a distraction from the work it
 * is protecting, so the ordinary state is one line of muted text — and the loud states are the two that
 * genuinely need a decision: somebody else has this draft open, or it is not being saved at all.
 */

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { CloudOff, Cloud, Loader2, Trash2, TriangleAlert } from "lucide-react";
import { ApiError } from "@/lib/api";
import { Button, Dialog, ErrorBanner, toast } from "@/components/ui";
import type { DraftAutosave, DraftResume } from "./use-draft-autosave";

/**
 * What went wrong resuming a draft, when something did.
 *
 * Both cases end the same way — the screen is a blank create form and the user carries on — so both say
 * so plainly rather than leaving somebody to wonder why their work did not come back.
 */
export function DraftNotices({ resume }: { resume: DraftResume }) {
  if (resume.unreadable) {
    return (
      <ErrorBanner
        message={
          "This draft was saved by an earlier version of this screen and can no longer be opened. "
          + "It has been left where it is — nothing has been deleted — but you will need to start again here."
        }
      />
    );
  }

  if (resume.error !== null) {
    return (
      <ErrorBanner
        message={
          resume.error.status === 404
            ? "That draft is no longer there — someone has discarded it, or it has already been raised."
            : resume.error.message
        }
        correlationId={resume.error.correlationId}
      />
    );
  }

  return null;
}

export function DraftStatus({ draft, noun, returnHref }: {
  draft: DraftAutosave;
  noun: string;
  /** Where discarding lands — the document's list. */
  returnHref: string;
}) {
  const router = useRouter();
  const [confirming, setConfirming] = useState(false);
  const [discarding, setDiscarding] = useState(false);
  const [failed, setFailed] = useState<ApiError | null>(null);

  /**
   * Discards, then leaves.
   *
   * <b>It used to stay put</b>, on the reasoning that "discard the draft" asks to delete the saved copy
   * and not to wipe the screen. That was wrong twice over: nothing visibly happened, so the button read
   * as broken — and the form left behind was no longer being saved, while the line under the heading
   * still said it was. Going back to the list is what "discard" plainly means.
   */
  async function discard() {
    setDiscarding(true);
    setFailed(null);
    try {
      await draft.discard();
      setConfirming(false);
      toast.success(`Draft ${noun} discarded.`);
      router.push(returnHref);
    } catch (e) {
      // Stay, and say so. Reporting it as discarded while the row is still there is the one outcome
      // worth avoiding — the user would go looking for it in the list and find it.
      setFailed(e as ApiError);
    } finally {
      setDiscarding(false);
    }
  }

  return (
    <>
      <div className="flex items-center justify-between gap-3 text-sm">
        <Indicator draft={draft} />

        {draft.draftId !== null && (
          <Button variant="ghost" size="sm" onClick={() => setConfirming(true)}>
            <Trash2 />
            Discard draft
          </Button>
        )}
      </div>

      <Dialog
        open={confirming}
        onOpenChange={(open) => !open && setConfirming(false)}
        title={`Discard this draft ${noun}?`}
        description={
          <>
            It is deleted and cannot be recovered, and you will be taken back to the list. Nothing has
            been raised, so nothing is cancelled — but the work typed into it is gone.
          </>
        }
        footer={
          <>
            <Button variant="secondary" onClick={() => setConfirming(false)}>
              Keep it
            </Button>
            <Button variant="danger" pending={discarding} onClick={discard}>
              Discard draft
            </Button>
          </>
        }
      >
        {failed && <ErrorBanner message={failed.message} correlationId={failed.correlationId} />}
      </Dialog>
    </>
  );
}

function Indicator({ draft }: { draft: DraftAutosave }) {
  // Re-renders on a timer so "saved 4 minutes ago" does not sit at "saved just now" indefinitely. Only
  // while a draft exists, and only once a minute — this is a clock, not an animation.
  const [, tick] = useState(0);

  useEffect(() => {
    if (draft.savedAt === null) return;

    const timer = setInterval(() => tick((n) => n + 1), 60_000);
    return () => clearInterval(timer);
  }, [draft.savedAt]);

  if (draft.status === "conflict") {
    return (
      <span className="flex items-center gap-2 text-warning-text">
        <TriangleAlert className="size-4 shrink-0" aria-hidden />
        <span>
          A colleague has edited this draft, so it has stopped saving here and cannot be raised from
          this page — otherwise you would both raise it and the customer would get two documents. Your
          work is still on screen: copy anything you need, then reload the draft to carry on from their
          version.
        </span>
      </span>
    );
  }

  if (draft.status === "error") {
    return (
      <span className="flex items-center gap-2 text-danger">
        <CloudOff className="size-4 shrink-0" aria-hidden />
        <span>
          Not saved — {draft.error?.message ?? "the last autosave failed."} Raise the document before
          leaving this page.
        </span>
      </span>
    );
  }

  if (draft.status === "saving") {
    return (
      <span className="flex items-center gap-2 text-muted">
        <Loader2 className="size-4 shrink-0 animate-spin" aria-hidden />
        Saving draft…
      </span>
    );
  }

  if (draft.savedAt !== null) {
    return (
      <span className="flex items-center gap-2 text-muted">
        <Cloud className="size-4 shrink-0" aria-hidden />
        Draft saved {agoLabel(draft.savedAt)}.
      </span>
    );
  }

  // Autosave has stopped and is not coming back — the draft was discarded, or its delete failed. Saying
  // "your work is saved as you type" here would be the exact false reassurance this feature exists to
  // remove: the user carries on typing, closes the tab, and loses everything.
  if (draft.stopped) {
    return (
      <span className="flex items-center gap-2 text-warning-text">
        <CloudOff className="size-4 shrink-0" aria-hidden />
        This is no longer being saved as a draft. Raise it, or copy anything you need before leaving.
      </span>
    );
  }

  return (
    <span className="text-muted">
      Your work is saved as a draft as you type — you can close this page and come back to it.
    </span>
  );
}

function agoLabel(at: Date): string {
  const minutes = Math.floor((Date.now() - at.getTime()) / 60_000);

  if (minutes < 1) return "just now";

  return new Intl.RelativeTimeFormat(undefined, { numeric: "auto" }).format(-minutes, "minute");
}
