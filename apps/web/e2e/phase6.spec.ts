import { test, expect } from "@playwright/test";
import { SEED } from "./seed";

/**
 * The Phase 6 acceptance flow, in a real browser against the real stack (PHASE-6-PLAN slice 5):
 * log in → raise a purchase order → record a supplier invoice and a partial payment (the derived payable
 * falls) → book a job card and close it (PENDING → CLOSED). All production code; only the database is a
 * throwaway. Numbering, the payables ledger and the guarded close are exercised end to end.
 */

async function login(page: import("@playwright/test").Page) {
  await page.goto("/login");
  await page.locator('input[name="username"]').fill(SEED.username);
  await page.locator('input[name="password"]').fill(SEED.password);
  await page.locator('input[name="password"]').press("Enter");
  await page.waitForURL((url) => url.pathname === "/");
}

/** Selects the seeded company on the current create form (targeting the option, not the topbar switcher). */
async function pickCompany(page: import("@playwright/test").Page) {
  const companySelect = page.locator("select").filter({ has: page.locator("option", { hasText: SEED.company }) });
  await companySelect.selectOption({ label: SEED.company });
}

test("raise a purchase order (item and service), on the shared engine", async ({ page }) => {
  await login(page);
  await page.goto("/purchase-orders/new");

  await pickCompany(page);

  await page.getByPlaceholder("Search suppliers…").fill(SEED.supplier);
  await page.getByRole("option", { name: new RegExp(SEED.supplier, "i") }).first().click();

  // Item document, then the seeded item at its master price ($100).
  await page.getByRole("button", { name: /Item document/i }).click();
  await page.getByLabel("Add an item").fill(SEED.item);
  await page.getByRole("option", { name: new RegExp(SEED.item, "i") }).first().click();

  await page.getByRole("button", { name: "Raise purchase order" }).click();

  await page.waitForURL(/\/purchase-orders\/\d+$/);
  const heading = await page.getByRole("heading", { name: /Purchase order\s+\S+/ }).first().innerText();
  expect(heading).toMatch(/E2EPO-/); // a transactional number was allocated
});

test("record a supplier invoice, then a partial payment settles part of the payable", async ({ page }) => {
  await login(page);
  await page.goto("/supplier-invoices/new");

  await pickCompany(page);
  await page.getByPlaceholder("Search suppliers…").fill(SEED.supplier);
  await page.getByRole("option", { name: new RegExp(SEED.supplier, "i") }).first().click();

  await page.getByLabel("Supplier's invoice no.").fill("SUP-E2E-1");
  await page.getByLabel("Amount (total)").fill("100");
  await page.getByRole("button", { name: "Record supplier invoice" }).click();

  // Read view: Pending, 100 outstanding.
  await page.waitForURL(/\/supplier-invoices\/\d+$/);
  await expect(page.locator("span").filter({ hasText: /^Pending$/ }).first()).toBeVisible();
  await expect(page.getByText(/Outstanding/).locator("xpath=following-sibling::*").first()).toContainText(/100(\.00)?/);

  // Record a partial payment of 30 → 70 outstanding, still Pending.
  await page.getByRole("button", { name: "Record payment" }).click();
  const dialog = page.getByRole("dialog");
  await dialog.getByLabel("Amount").fill("30");
  await dialog.getByRole("button", { name: "Record payment" }).click();

  await expect(page.getByText(/Outstanding/).locator("xpath=following-sibling::*").first()).toContainText(/70(\.00)?/);
  await expect(page.locator("span").filter({ hasText: /^Pending$/ }).first()).toBeVisible();
});

test("book a job card with a serial line and close it (PENDING to CLOSED)", async ({ page }) => {
  await login(page);
  await page.goto("/job-cards/new");

  await pickCompany(page);
  await page.getByPlaceholder("Search customers…").fill(SEED.customer);
  await page.getByRole("option", { name: new RegExp(SEED.customer, "i") }).first().click();

  await page.getByPlaceholder("Item / description").first().fill("Dell Latitude");
  await page.getByPlaceholder("Serial no.").first().fill("SN-E2E-1");

  await page.getByRole("button", { name: "Raise job card" }).click();

  // Creating a job card redirects into the print overlay, so the URL carries ?print=1.
  await page.waitForURL(/\/job-cards\/\d+/);
  await expect(page.locator("span").filter({ hasText: /^Pending$/ }).first()).toBeVisible();

  // Close it — cost, sell and what was done.
  await page.getByRole("button", { name: "Close job" }).click();
  const dialog = page.getByRole("dialog");
  await dialog.getByLabel("Cost").fill("120");
  await dialog.getByLabel("Sell").fill("200");
  // The close dialog asks for optional completion remarks — there is no reason field on it.
  await dialog.getByLabel("Completion remarks").fill("Replaced the mainboard and tested");
  await dialog.getByRole("button", { name: "Close job" }).click();

  await expect(page.locator("span").filter({ hasText: /^Closed$/ }).first()).toBeVisible();
});
