import { describe, expect, it } from "vitest";
import { formatInstant, instantFromApi } from "./time";

/**
 * The API stores UTC and does not always say so — a value read back out of MySQL arrives without a zone.
 * Read as local time it produces a clock out by the whole offset, which looks plausible and is wrong.
 * That is the bug these hold: 06:53 UTC is 12:23 in Colombo, and the Backups list was showing 06:53.
 */
describe("instantFromApi", () => {
  it("treats a timestamp with no zone as UTC, not as local", () => {
    expect(instantFromApi("2026-07-21T06:53:00")?.toISOString()).toBe("2026-07-21T06:53:00.000Z");
  });

  it("leaves an explicit Z alone", () => {
    expect(instantFromApi("2026-07-21T06:53:00Z")?.toISOString()).toBe("2026-07-21T06:53:00.000Z");
  });

  it("leaves an explicit offset alone rather than appending a Z to it", () => {
    // The naive `endsWith("Z") ? v : v + "Z"` guard turns "…+05:30" into "…+05:30Z", which is not a date
    // at all. Same instant, stated two ways: 12:23 in Colombo is 06:53 UTC.
    expect(instantFromApi("2026-07-21T12:23:00+05:30")?.toISOString()).toBe("2026-07-21T06:53:00.000Z");
    expect(instantFromApi("2026-07-21T12:23:00+0530")?.toISOString()).toBe("2026-07-21T06:53:00.000Z");
  });

  it("handles fractional seconds, which is how the API sends datetime(6)", () => {
    expect(instantFromApi("2026-07-21T06:53:00.123456")?.toISOString())
      .toBe("2026-07-21T06:53:00.123Z");
  });

  it("returns null for nothing, rather than an Invalid Date that renders as gibberish", () => {
    expect(instantFromApi(null)).toBeNull();
    expect(instantFromApi(undefined)).toBeNull();
    expect(instantFromApi("")).toBeNull();
    expect(instantFromApi("not a date")).toBeNull();
  });
});

describe("formatInstant", () => {
  it("renders in the reader's zone — 06:53 UTC is 12:23 in Colombo", () => {
    const shown = instantFromApi("2026-07-21T06:53:00")!.toLocaleString("en-GB", {
      timeZone: "Asia/Colombo",
      dateStyle: "short",
      timeStyle: "short",
    });

    expect(shown).toContain("12:23");
  });

  it("shows a dash rather than 'Invalid Date' when there is nothing to show", () => {
    expect(formatInstant(null)).toBe("—");
    expect(formatInstant("nonsense")).toBe("—");
  });
});
