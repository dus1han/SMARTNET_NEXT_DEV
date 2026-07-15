import { fileURLToPath } from "node:url";
import { defineConfig } from "vitest/config";

/**
 * Unit tests for the parts of the frontend where a bug costs money.
 *
 * Not a general frontend test suite — DEVELOPMENT.md §9 puts UI coverage in Playwright and the
 * business rules in `Smartnet.Tests`, where the server (which is the authority) enforces them. What
 * runs here is the arithmetic the browser does *for display*: the figure under the user's cursor
 * must be the figure they are invoiced for, and in a codebase whose central defect is money-as-double
 * (ISSUES B1), that is not something to eyeball.
 */
export default defineConfig({
  test: {
    environment: "node",
    include: ["src/**/*.test.ts"],
  },
  resolve: {
    alias: {
      "@": fileURLToPath(new URL("./src", import.meta.url)),
    },
  },
});
