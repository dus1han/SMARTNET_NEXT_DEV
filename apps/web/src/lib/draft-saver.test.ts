import { describe, expect, it, vi } from "vitest";
import { createDraftSaver, type SendDraft } from "./draft-saver";
import type { DraftSaved, SaveDraftRequest } from "./drafts";

/**
 * Autosave exists to stop people losing work. Every test here is a way it could lose some anyway while
 * still showing "Draft saved" — which is worse than not having the feature, because the reassurance is
 * what stops somebody re-typing it.
 */

const request = (payload: string): SaveDraftRequest => ({
  docType: "INVOICE",
  payload,
  partyName: "Acme Trading",
  total: 100,
  lineCount: 1,
});

/** A `send` that resolves when the test says so, so two saves can genuinely overlap. */
function deferredSend() {
  const calls: { request: SaveDraftRequest; resolve: (saved: DraftSaved) => void; reject: (e: unknown) => void }[] = [];

  const send: SendDraft = (_target, req) =>
    new Promise<DraftSaved>((resolve, reject) => {
      calls.push({ request: req, resolve, reject });
    });

  return { send, calls };
}

const events = () => ({ onSaving: vi.fn(), onSaved: vi.fn(), onFailed: vi.fn() });

/**
 * Lets the queued follow-up start.
 *
 * `save()` resolves only once the whole chain has drained — including anything queued behind it — so a
 * test cannot await it to observe the follow-up being *sent*. It has to yield, look, then release.
 */
const tick = () => new Promise((resolve) => setTimeout(resolve, 0));

const saved = (id: number, rowVersion: number): DraftSaved => ({
  id,
  rowVersion,
  updatedAt: "2026-07-22T10:00:00",
});

describe("createDraftSaver", () => {
  it("creates on the first save and updates on the next, carrying the version forward", async () => {
    const sent: (({ id: number; rowVersion: number }) | null)[] = [];
    const send: SendDraft = (target) => {
      sent.push(target);
      return Promise.resolve(saved(5, sent.length));
    };

    const saver = createDraftSaver(send, events());

    saver.setPending(request("one"));
    await saver.save();

    saver.setPending(request("two"));
    await saver.save();

    // Null means INSERT. The second must carry the id and the version the first came back with —
    // without that, every autosave would leave another copy of the same draft in the list.
    expect(sent).toEqual([null, { id: 5, rowVersion: 1 }]);
  });

  it("does not lose an edit made while a save is in flight", async () => {
    // The bug this was written for. A slow request with typing underneath it used to drop the typing,
    // and the indicator still said "saved" — so the user closed the tab believing it was.
    const { send, calls } = deferredSend();
    const saver = createDraftSaver(send, events());

    saver.setPending(request("first"));
    const inFlight = saver.save();

    // The user carries on typing while the request is still out.
    saver.setPending(request("second"));
    await saver.save();

    expect(calls).toHaveLength(1);

    calls[0].resolve(saved(5, 1));
    await tick();

    // The queued save ran, and sent the *latest* state rather than the one it was scheduled with.
    expect(calls).toHaveLength(2);
    expect(calls[1].request.payload).toBe("second");

    calls[1].resolve(saved(5, 2));
    await inFlight;
  });

  it("collapses several edits made during one in-flight save into a single follow-up", async () => {
    const { send, calls } = deferredSend();
    const saver = createDraftSaver(send, events());

    saver.setPending(request("first"));
    const inFlight = saver.save();

    for (const payload of ["a", "b", "c"]) {
      saver.setPending(request(payload));
      await saver.save();
    }

    calls[0].resolve(saved(5, 1));
    await tick();

    // One follow-up, holding the last state — not three requests racing each other to the server.
    expect(calls).toHaveLength(2);
    expect(calls[1].request.payload).toBe("c");

    calls[1].resolve(saved(5, 2));
    await inFlight;
  });

  it("sends nothing when the form has not changed since the last save", async () => {
    const send = vi.fn<SendDraft>().mockResolvedValue(saved(5, 1));
    const saver = createDraftSaver(send, events());

    saver.setPending(request("same"));
    await saver.save();
    await saver.save();
    await saver.save();

    // Otherwise every tab switch and every unmount would bump row_version and the "left by" name.
    expect(send).toHaveBeenCalledTimes(1);
  });

  it("stops for good on a conflict, and reports it as one", async () => {
    const send = vi.fn<SendDraft>().mockRejectedValue({ status: 409 });
    const on = events();
    const saver = createDraftSaver(send, on);

    saver.setPending(request("mine"));
    await saver.save();

    expect(on.onFailed).toHaveBeenCalledWith({ status: 409 }, true);
    expect(saver.snapshot().stopped).toBe(true);

    // The point of stopping: a colleague has this draft open and saved first. Carrying on would
    // overwrite their work at the next attempt, and both of them would think they were being saved.
    saver.setPending(request("mine, again"));
    await saver.save();

    expect(send).toHaveBeenCalledTimes(1);
  });

  it("keeps trying after an ordinary failure, which is not terminal", async () => {
    const send = vi.fn<SendDraft>()
      .mockRejectedValueOnce({ status: 500 })
      .mockResolvedValueOnce(saved(5, 1));
    const on = events();
    const saver = createDraftSaver(send, on);

    saver.setPending(request("one"));
    await saver.save();

    expect(on.onFailed).toHaveBeenCalledWith({ status: 500 }, false);
    expect(saver.snapshot().stopped).toBe(false);

    // A dropped connection must not permanently disarm autosave — the next pause in typing retries.
    saver.setPending(request("two"));
    await saver.save();

    expect(send).toHaveBeenCalledTimes(2);
    expect(on.onSaved).toHaveBeenCalledTimes(1);
  });

  it("sends nothing more once the document has been raised", async () => {
    const send = vi.fn<SendDraft>().mockResolvedValue(saved(5, 1));
    const saver = createDraftSaver(send, events());

    saver.setPending(request("one"));
    await saver.save();

    expect(saver.stop()).toBe(5);

    // The unmount flush fires right after the screen navigates away. If it re-created the draft, every
    // raised document would leave a ghost of itself in the Drafts tab.
    saver.setPending(request("two"));
    await saver.save();

    expect(send).toHaveBeenCalledTimes(1);
    expect(saver.snapshot().id).toBeNull();
  });

  it("reports an adopted draft as an update, never an insert", async () => {
    const sent: (({ id: number; rowVersion: number }) | null)[] = [];
    const send: SendDraft = (target) => {
      sent.push(target);
      return Promise.resolve(saved(9, 4));
    };

    const saver = createDraftSaver(send, events());

    // Resuming `?draft=9`, loaded at version 3.
    saver.adopt(9, 3);
    saver.setPending(request("resumed"));
    await saver.save();

    expect(sent).toEqual([{ id: 9, rowVersion: 3 }]);
  });

  it("knows whether there is anything unsaved, which is what a flush decides on", async () => {
    const send = vi.fn<SendDraft>().mockResolvedValue(saved(5, 1));
    const saver = createDraftSaver(send, events());

    expect(saver.hasUnsaved()).toBe(false);

    saver.setPending(request("typed"));
    expect(saver.hasUnsaved()).toBe(true);

    await saver.save();
    expect(saver.hasUnsaved()).toBe(false);
  });
});
