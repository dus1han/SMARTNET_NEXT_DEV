import { test, expect } from "@playwright/test";
import { SEED } from "./seed";

/**
 * The Phase 7 acceptance flow, in a real browser against the real stack (PHASE-7-PLAN slice 6):
 * log in → raise two invoices for one customer → take ONE receipt allocated across both → see both
 * derived balances fall → record a cheque and an expense.
 *
 * That first flow is the phase's exit criterion, and it is the whole of what the legacy payment screen
 * got wrong. The old `savePay` inserted a payments row and then separately decremented
 * `invoice_h.balance`, with nothing joining the two and no idempotency — the mechanism behind Finding
 * 1's Rs 1.55M of duplicate payments. Here the allocation, the ledger entries and the legacy shadow all
 * go through the production code; only the database is a throwaway.
 *
 * <b>This spec is why `POST /api/dev/seed-payment` could be deleted.</b> Until it existed, the Phase 5
 * E2E had no payments UI to drive and seeded a ledger row through an anonymous Development-only
 * endpoint that wrote real money. Payments are now taken the way a user takes them.
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

/** Raises one credit invoice for the seeded customer at the item's master price, and returns its number. */
async function raiseInvoice(page: import("@playwright/test").Page) {
  await page.goto("/invoices/new");
  await pickCompany(page);

  await page.getByPlaceholder("Search customers…").click();
  await page.getByPlaceholder("Search customers…").fill(SEED.customer);
  await page.getByRole("option", { name: new RegExp(SEED.customer, "i") }).first().click();

  await page.getByRole("button", { name: /Item document/i }).click();
  await page.getByLabel("Add an item").fill(SEED.item);
  await page.getByRole("option", { name: new RegExp(SEED.item, "i") }).first().click();

  await page.getByRole("button", { name: "Raise invoice" }).click();
  await page.waitForURL(/\/invoices\/\d+$/);

  const heading = await page.getByRole("heading", { name: /Invoice\s+\S+/ }).first().innerText();
  const number = heading.replace(/^Invoice\s+/i, "").trim();
  const url = page.url();

  // 100 net + 18% VAT, on credit, so the whole 118 is outstanding.
  await expect(page.locator(".text-3xl").first()).toContainText(/118(\.00)?/);

  return { number, url };
}

test("one receipt allocated across two invoices settles both", async ({ page }) => {
  await login(page);

  const first = await raiseInvoice(page);
  const second = await raiseInvoice(page);
  expect(first.number).not.toBe(second.number);

  // --- One receipt, split across both invoices -------------------------------------------------
  await page.goto("/payments/new");
  await pickCompany(page);

  await page.getByPlaceholder("Search customers…").click();
  await page.getByPlaceholder("Search customers…").fill(SEED.customer);
  await page.getByRole("option", { name: new RegExp(SEED.customer, "i") }).first().click();

  // Allocated by invoice number, not by row position — this customer carries invoices from the other
  // specs in the same throwaway database, so "the first open invoice" is not a stable target.
  await page.getByLabel(`Allocate to ${first.number}`).fill("50");
  await page.getByLabel(`Allocate to ${second.number}`).fill("30");

  await page.getByRole("button", { name: "Record receipt" }).click();
  await page.waitForURL(/\/payments\/\d+$/);

  // The receipt is the sum of its allocations, and both invoices are on it.
  await expect(page.getByRole("heading", { name: /Receipt · .*80(\.00)?/ })).toBeVisible();
  await expect(page.getByText(first.number, { exact: true })).toBeVisible();
  await expect(page.getByText(second.number, { exact: true })).toBeVisible();

  // --- Both derived balances fell, each by its own allocation ----------------------------------
  await page.goto(first.url);
  await expect(page.locator(".text-3xl").first()).toContainText(/68(\.00)?/); // 118 − 50

  await page.goto(second.url);
  await expect(page.locator(".text-3xl").first()).toContainText(/88(\.00)?/); // 118 − 30
});

test("a receipt shows on the invoice it settled", async ({ page }) => {
  await login(page);

  const invoice = await raiseInvoice(page);

  await page.goto("/payments/new");
  await pickCompany(page);
  await page.getByPlaceholder("Search customers…").click();
  await page.getByPlaceholder("Search customers…").fill(SEED.customer);
  await page.getByRole("option", { name: new RegExp(SEED.customer, "i") }).first().click();

  await page.getByLabel("Method").selectOption("CHEQUE");
  await page.getByLabel("Reference").fill("CHQ-E2E-77");
  await page.getByLabel(`Allocate to ${invoice.number}`).fill("18");

  await page.getByRole("button", { name: "Record receipt" }).click();
  await page.waitForURL(/\/payments\/\d+$/);

  // The payment is visible on the invoice, not only on the receipt — the detail behind the balance.
  await page.goto(invoice.url);
  await expect(page.locator(".text-3xl").first()).toContainText(/100(\.00)?/); // 118 − 18
  await expect(page.getByText("CHQ-E2E-77")).toBeVisible();
});

test("record a cheque", async ({ page }) => {
  await login(page);
  await page.goto("/cheques/new");

  await pickCompany(page);
  await page.getByLabel("Pay to").fill("E2E Payee");
  await page.getByLabel("Bank").fill("E2E Bank");
  await page.getByLabel("Cheque no.").fill("100077");
  await page.getByLabel("Amount").fill("250");

  await page.getByRole("button", { name: "Record cheque" }).click();

  // The register redirects straight into the print overlay, so the id is what matters here.
  await page.waitForURL(/\/cheques\/\d+/);
  await expect(page.getByRole("heading", { name: /Cheque · .*250(\.00)?/ })).toBeVisible();
});

test("record an expense against a category", async ({ page }) => {
  await login(page);

  // Nothing seeds expense categories, and the form cannot be submitted without one — so the category
  // is created through its own UI rather than reaching past it into the database.
  await page.goto("/expenses");
  await page.getByRole("button", { name: "Categories" }).click();

  const categories = page.getByRole("dialog");
  await categories.getByLabel("New category").fill("E2E Category");
  await categories.getByRole("button", { name: "Add", exact: true }).click();
  // The new category appears as the value of its rename input, not as static text.
  await expect(categories.getByRole("textbox").nth(1)).toHaveValue("E2E Category");
  await categories.getByRole("button", { name: "Done" }).click();

  await page.goto("/expenses/new");
  await pickCompany(page);
  await page.getByLabel("Category").selectOption({ label: "E2E Category" });
  await page.getByLabel("Description").fill("E2E stationery");
  await page.getByLabel("Net (before VAT)").fill("40");

  await page.getByRole("button", { name: "Record expense" }).click();

  await page.waitForURL(/\/expenses$/);
  await expect(page.getByText("E2E stationery")).toBeVisible();
});
