import { defineConfig, devices } from "@playwright/test";

/**
 * The end-to-end harness (PHASE-5-PLAN slice 6).
 *
 * `global-setup` stands the whole stack up against a THROWAWAY MariaDB — nothing here touches the dev
 * or production database:
 *   1. `tools/E2EHost` starts a disposable MariaDB, applies the real schema + migrations, seeds a
 *      user / company / tax-rate / series / customer / item, and prints its connection string.
 *   2. the real API is launched against that connection string on :5099.
 *   3. `next dev` is launched on :3100, pointed at that API.
 * `global-teardown` kills all three (and the container goes with the host; Testcontainers' reaper is
 * the backstop). The run is hermetic and CI-safe.
 */
export default defineConfig({
  testDir: "./e2e",
  testMatch: "**/*.spec.ts",
  timeout: 90_000,
  expect: { timeout: 20_000 },
  fullyParallel: false,
  workers: 1,
  retries: 0,
  reporter: [["list"]],
  globalSetup: "./e2e/global-setup.ts",
  globalTeardown: "./e2e/global-teardown.ts",
  use: {
    baseURL: process.env.E2E_WEB_URL ?? "http://localhost:3100",
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
  },
  projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"] } }],
});
