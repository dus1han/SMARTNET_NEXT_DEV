import { test, expect } from "@playwright/test";
import { SEED, API_URL } from "./seed";

/**
 * The slice-6 acceptance flow, in a real browser against the real stack (PHASE-5-PLAN slice 6):
 * log in → raise an invoice through the create form → take a partial payment → the derived balance
 * reflects it. The tax engine, the number allocator, the ledger and the credit-limit gate are all the
 * production code; only the database is a throwaway, and only the payment is seeded (there is no
 * payments UI until Phase 7).
 */
test("login, raise an invoice, take a partial payment, and see the derived balance", async ({ page, request }) => {
  // --- Log in through the UI -------------------------------------------------------------------
  await page.goto("/login");
  await page.locator('input[name="username"]').fill(SEED.username);
  await page.locator('input[name="password"]').fill(SEED.password);
  await page.locator('input[name="password"]').press("Enter");
  await page.waitForURL((url) => url.pathname === "/"); // the app shell (dashboard)

  // --- Raise an invoice: company, customer, one item line ($100), credit --------------------------
  await page.goto("/invoices/new");

  // The company field is the native <select> carrying the seeded company as an option (targeting it by
  // the option avoids clashing with the topbar company switcher, which shares the word "company").
  const companySelect = page
    .locator("select")
    .filter({ has: page.locator("option", { hasText: SEED.company }) });
  await companySelect.selectOption({ label: SEED.company });

  await page.getByPlaceholder("Search customers…").click();
  await page.getByPlaceholder("Search customers…").fill(SEED.customer);
  await page.getByRole("option", { name: new RegExp(SEED.customer, "i") }).first().click();

  // Item document, then pick the seeded item — it comes in at its master price ($100).
  await page.getByRole("button", { name: /Item document/i }).click();
  await page.getByLabel("Add an item").fill(SEED.item);
  await page.getByRole("option", { name: new RegExp(SEED.item, "i") }).first().click();

  // Save. Total = 100 net + 18% VAT = 118; credit, so all of it is outstanding.
  await page.getByRole("button", { name: "Raise invoice" }).click();

  // Landed on the read view; capture the allocated number from the heading ("Invoice E2E-1").
  await page.waitForURL(/\/invoices\/\d+$/);
  const heading = await page.getByRole("heading", { name: /Invoice\s+\S+/ }).first().innerText();
  const number = heading.replace(/^Invoice\s+/i, "").trim();
  expect(number).toMatch(/E2E-/);

  // The outstanding figure is the view's headline number (the only `.text-3xl` on the page). On a
  // fresh credit invoice the whole 118 is outstanding.
  const outstandingValue = page.locator(".text-3xl").first();
  await expect(outstandingValue).toContainText(/118(\.00)?/);

  // --- Seed a partial payment of 50 (the Phase 7 payments UI does not exist yet) -----------------
  const seed = await request.post(`${API_URL}/api/dev/seed-payment`, {
    data: { invoiceNumber: number, amount: 50 },
  });
  expect(seed.ok()).toBeTruthy();

  // --- The derived balance reflects it: 118 − 50 = 68 -------------------------------------------
  await page.reload();
  await expect(page.locator(".text-3xl").first()).toContainText(/68(\.00)?/);
});
