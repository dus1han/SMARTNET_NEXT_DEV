import { describe, expect, it } from "vitest";
import { isCredentialCheck, safeReturnPath } from "./session";

/**
 * The return path is attacker-controlled: it arrives in a query string, on the one screen where a user
 * is about to type a password. So it is tested like input, not like a convenience.
 */
describe("safeReturnPath", () => {
  it("keeps a path on this origin, query string and all", () => {
    expect(safeReturnPath("/invoices/42?tab=lines")).toBe("/invoices/42?tab=lines");
  });

  it("decodes what endSession encoded", () => {
    expect(safeReturnPath(encodeURIComponent("/reports/sales?from=2026-01-01"))).toBe(
      "/reports/sales?from=2026-01-01",
    );
  });

  it("falls back to the dashboard when there is nothing to go back to", () => {
    expect(safeReturnPath(null)).toBe("/");
    expect(safeReturnPath("")).toBe("/");
  });

  it("refuses an absolute URL", () => {
    // Otherwise the sign-in form is an open redirect: a phishing link that passes through the real
    // site and lands somewhere else is worth far more than one that never touches it.
    expect(safeReturnPath("https://evil.test/harvest")).toBe("/");
    expect(safeReturnPath(encodeURIComponent("https://evil.test/harvest"))).toBe("/");
  });

  it("refuses a protocol-relative URL", () => {
    // `//evil.test` is a URL, not a path — the browser fills in the current scheme. It starts with a
    // slash, so a naive "must start with /" check waves it straight through.
    expect(safeReturnPath("//evil.test")).toBe("/");
    expect(safeReturnPath(encodeURIComponent("//evil.test/x"))).toBe("/");
  });

  it("refuses anything that is not a path at all", () => {
    expect(safeReturnPath("javascript:alert(1)")).toBe("/");
    expect(safeReturnPath("invoices")).toBe("/");
  });

  it("survives a malformed encoding rather than throwing", () => {
    // decodeURIComponent throws on a lone %; a crash here would break the sign-in screen itself.
    expect(safeReturnPath("%")).toBe("/");
  });
});

describe("isCredentialCheck", () => {
  it("recognises the sign-in request", () => {
    // Its 401 means "wrong password" and must reach the form. Treating it as an expired session would
    // reload the page and the user would never learn what happened.
    expect(isCredentialCheck("/api/auth/login")).toBe(true);
  });

  it("does not recognise ordinary requests", () => {
    expect(isCredentialCheck("/api/auth/me")).toBe(false);
    expect(isCredentialCheck("/api/invoices")).toBe(false);
  });
});
