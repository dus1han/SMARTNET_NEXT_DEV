import { describe, expect, it } from "vitest";
import { ACTIVE_WINDOW_MS, activeElsewhere, readPayload, writePayload } from "./drafts";

/**
 * Drafts are shared, so two people can open one. The badge on the Drafts list is the only warning
 * anybody gets before they start typing — after that, the first to save wins and the other is locked
 * out of raising it. So it has to warn about the right person.
 */
describe("activeElsewhere", () => {
  // The API sends UTC without a zone, which is why this goes through instantFromApi rather than Date.
  const at = (msAgo: number) => new Date(Date.now() - msAgo).toISOString().replace("Z", "");
  const now = Date.now();

  it("warns when a colleague saved it moments ago", () => {
    expect(activeElsewhere({ updatedById: 2, updatedAt: at(5_000) }, 1, now)).toBe(true);
  });

  it("says nothing about a draft you saved yourself", () => {
    // Your own autosave fires every few seconds. A badge on your own row would fire constantly and
    // teach people to ignore it — which would cost them the one time it mattered.
    expect(activeElsewhere({ updatedById: 1, updatedAt: at(5_000) }, 1, now)).toBe(false);
  });

  it("stops warning once the draft has gone quiet", () => {
    expect(activeElsewhere({ updatedById: 2, updatedAt: at(ACTIVE_WINDOW_MS + 1_000) }, 1, now))
      .toBe(false);
  });

  it("warns a signed-out or unknown viewer rather than staying silent", () => {
    // No viewer id means we cannot prove it is not somebody else, and silence is the worse guess.
    expect(activeElsewhere({ updatedById: 2, updatedAt: at(5_000) }, null, now)).toBe(true);
  });

  it("says nothing when there is no author or no usable timestamp", () => {
    expect(activeElsewhere({ updatedById: null, updatedAt: at(5_000) }, 1, now)).toBe(false);
    expect(activeElsewhere({ updatedById: 2, updatedAt: "not a date" }, 1, now)).toBe(false);
  });
});

/**
 * A draft is written by one deployment of a create screen and read back by whichever one happens to be
 * running when the user returns. That gap is the whole risk: fields get added, renamed and repurposed,
 * and a payload restored into a form that has moved on does not look broken — it looks filled in.
 *
 * So `readPayload` refuses anything it does not recognise, and returns null rather than a half-state.
 * These hold that refusal, because the failure it prevents is silent by nature.
 */
describe("writePayload / readPayload", () => {
  interface State {
    customerId: string;
    lines: { description: string; quantity: number }[];
  }

  const state: State = {
    customerId: "42",
    lines: [{ description: "Router install", quantity: 2000 }],
  };

  it("round-trips a screen's state unchanged", () => {
    expect(readPayload<State>(writePayload(1, state), 1)).toEqual(state);
  });

  it("refuses a payload written by a different version of the screen", () => {
    // The case this exists for: version 1 stored a `contact` string, version 2 made it a list. Reading
    // one as the other fills the form with something that is not what was typed.
    expect(readPayload<State>(writePayload(1, state), 2)).toBeNull();
  });

  it("refuses something that is not JSON at all", () => {
    expect(readPayload<State>("not json {", 1)).toBeNull();
  });

  it("refuses a payload whose state is missing", () => {
    expect(readPayload<State>(JSON.stringify({ v: 1 }), 1)).toBeNull();
  });

  it("refuses a payload whose state is not an object", () => {
    // A bare string or number would sail through a `!= null` check and then be spread into the form.
    expect(readPayload<State>(JSON.stringify({ v: 1, state: "quotation" }), 1)).toBeNull();
    expect(readPayload<State>(JSON.stringify({ v: 1, state: 7 }), 1)).toBeNull();
  });

  it("refuses a null state, which JSON.parse reports as an object", () => {
    // `typeof null === "object"` is the trap here: the guard has to check for null separately, and this
    // is what catches it if somebody simplifies the condition.
    expect(readPayload<State>(JSON.stringify({ v: 1, state: null }), 1)).toBeNull();
  });

  it("keeps an empty state, which is a real thing to have typed", () => {
    // Not the same as a missing one: a user can legitimately empty a draft they had filled in, and that
    // edit has to be savable — otherwise clearing a mistake leaves the mistake on file.
    expect(readPayload<Record<string, never>>(writePayload(1, {}), 1)).toEqual({});
  });

  it("writes valid JSON, which the server requires before it will store it", () => {
    // The server parses the payload once, purely to check it can be read back — see DraftsController.
    // A payload it rejects would fail the autosave, not the resume, which is a confusing place to find out.
    expect(() => JSON.parse(writePayload(1, state))).not.toThrow();
  });
});
