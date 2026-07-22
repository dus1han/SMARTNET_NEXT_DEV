import { test, expect, type Page } from "@playwright/test";
import { API_URL, SEED } from "./seed";

/**
 * Draft autosave, and the concurrent-edit guard, in a real browser against the real stack.
 *
 * Two things here were shipped without ever being exercised signed in, which is why this file exists:
 *
 *   1. **Resuming a draft.** The first version read `?draft=` from `window.location.search`, which is
 *      not yet updated when a client-side navigation mounts the page — so the fetch never happened and
 *      the form came up blank with nothing to explain it. Only the API log showed it: a POST and a PUT
 *      for the draft, and never a GET. The test below checks the restored *fields*, because "the page
 *      loaded" was true throughout the bug.
 *   2. **The version check on master data.** Those saves now refuse a stale or missing version, which
 *      means a screen that forgets to send one is broken rather than merely unprotected.
 */

async function login(page: Page) {
  await page.goto("/login");
  await page.locator('input[name="username"]').fill(SEED.username);
  await page.locator('input[name="password"]').fill(SEED.password);
  await page.locator('input[name="password"]').press("Enter");
  await page.waitForURL((url) => url.pathname === "/");
}

/** The seeded company's id, for the `X-Company-Id` header on direct API calls. */
async function companyId(page: Page): Promise<number> {
  const response = await page.request.get(`${API_URL}/api/companies`);
  expect(response.ok(), "listing companies").toBeTruthy();

  const company = (await response.json()).find((c: { name: string }) => c.name === SEED.company);
  expect(company, "the seeded company").toBeTruthy();

  return company.id as number;
}

/**
 * Removes every quotation draft, so a test starts from a known-empty list.
 *
 * Drafts are shared and survive the session by design — which is the feature, and which means one spec
 * leaves work behind for the next. Without this, "No drafts" only holds for whichever test runs first.
 */
async function clearDrafts(page: Page, company: number) {
  const headers = { "X-Company-Id": String(company) };
  const list = await page.request.get(`${API_URL}/api/drafts?docType=QUOTATION`, { headers });
  expect(list.ok(), "listing drafts").toBeTruthy();

  for (const draft of await list.json()) {
    await page.request.delete(`${API_URL}/api/drafts/${draft.id}`, { headers });
  }
}

/** Fills a quotation far enough to be worth keeping, and waits for the draft to actually be stored. */
async function startQuotation(page: Page) {
  await page.goto("/quotations/new");

  await page
    .locator("select")
    .filter({ has: page.locator("option", { hasText: SEED.company }) })
    .selectOption({ label: SEED.company });

  await page.getByPlaceholder("Search customers…").fill(SEED.customer);
  await page.getByRole("option", { name: new RegExp(SEED.customer, "i") }).first().click();

  await page.getByRole("button", { name: /Item document/i }).click();
  await page.getByLabel("Add an item").fill(SEED.item);
  await page.getByRole("option", { name: new RegExp(SEED.item, "i") }).first().click();

  // The indicator is the screen's own claim that the work is safe. Waiting on it rather than on a fixed
  // sleep also means the debounce and its ceiling are part of what is being tested.
  await expect(page.getByText(/Draft saved/i)).toBeVisible({ timeout: 20_000 });
}

/** Opens the Drafts half of the quotation list. */
async function openDraftsTab(page: Page) {
  await page.goto("/quotations");
  await page.getByRole("button", { name: /^Drafts/ }).click();
}

test.beforeEach(async ({ page }) => {
  await login(page);
  await clearDrafts(page, await companyId(page));
});

test("a quotation is kept as a draft, and resuming it brings the work back", async ({ page }) => {
  await startQuotation(page);

  // Leave without raising it — the whole point of the feature.
  await openDraftsTab(page);

  const row = page.getByRole("row").filter({ hasText: SEED.customer });
  await expect(row).toBeVisible();
  await row.click();

  await expect(page).toHaveURL(/\/quotations\/new\?draft=\d+/);
  await expect(page.getByRole("heading", { name: "Draft quotation" })).toBeVisible();

  // The regression this file exists for. Reaching the page was never the broken part — the fields were.
  // They are input *values*, not text, which is why these are toHaveValue and not toBeVisible.
  await expect(page.getByPlaceholder("Search customers…")).toHaveValue(new RegExp(SEED.customer));
  await expect(page.getByRole("textbox", { name: /^Description of line/ })).toHaveValue(SEED.item);
  await expect(page.getByRole("textbox", { name: new RegExp(`Quantity of ${SEED.item}`) })).toHaveValue("1");
  await expect(page.getByRole("textbox", { name: new RegExp(`Unit price of ${SEED.item}`) }))
    .toHaveValue(SEED.itemPrice.toFixed(2));
});

test("raising a draft clears it from the Drafts tab", async ({ page }) => {
  await startQuotation(page);

  await page.getByRole("button", { name: "Raise quotation" }).click();
  await page.waitForURL(/\/quotations\/\d+$/);

  await openDraftsTab(page);

  // The draft became a document, so it must not also still be a draft — otherwise every raise leaves a
  // ghost of itself behind and the list stops being worth reading.
  await expect(page.getByText("No drafts")).toBeVisible();
});

test("a draft can be discarded from the list", async ({ page }) => {
  await startQuotation(page);
  await openDraftsTab(page);

  await page.getByRole("button", { name: /Discard the draft/i }).first().click();
  await page.getByRole("button", { name: "Discard draft" }).click();

  await expect(page.getByText("No drafts")).toBeVisible();
});

/**
 * The concurrency guard, against the endpoint rather than through two browsers.
 *
 * Driving two real sessions would test the same server rule far more slowly and far more flakily; what
 * matters is that the endpoint refuses a version it has already moved past.
 */
test("a customer edit carrying a stale version is refused", async ({ page }) => {
  const headers = { "X-Company-Id": String(await companyId(page)) };

  // A customer of its own, not the seeded one.
  //
  // Every spec in this run shares one throwaway database, so renaming SEED.customer here made it
  // unfindable in the invoice, phase6 and phase7 specs — four failures in code this change never
  // touched. A test that mutates shared fixture data is a test that breaks its neighbours.
  const created = await page.request.post(`${API_URL}/api/customers`, {
    headers,
    data: { name: "Concurrency Test Co", creditLimit: 0 },
  });
  expect(created.ok(), "creating the customer this test will edit").toBeTruthy();

  const id = (await created.json()).id as number;

  const list = await page.request.get(`${API_URL}/api/customers`, { headers });
  expect(list.ok()).toBeTruthy();

  const customer = (await list.json()).find((c: { id: number }) => c.id === id);
  expect(customer, "the customer just created").toBeTruthy();

  const save = (rowVersion: number | undefined, name: string) =>
    page.request.put(`${API_URL}/api/customers/${customer.id}`, {
      headers,
      data: {
        name,
        type: customer.type,
        contactPerson: customer.contactPerson,
        address: customer.address,
        phone: customer.phone,
        email: customer.email,
        vatNumber: customer.vatNumber,
        assignedCompanyId: customer.assignedCompanyId,
        profitPercentId: customer.profitPercentId,
        creditLimit: customer.creditLimit,
        expectedRowVersion: rowVersion,
      },
    });

  // The version both "users" loaded.
  const loaded = customer.rowVersion as number;

  expect((await save(loaded, "Concurrency Test Co (first)")).status(), "an edit carrying the current version")
    .toBe(204);

  // The second user still holds the version from before that save. Without the guard this lands and the
  // first edit is gone with no error anywhere — the exact behaviour this was built to end.
  expect((await save(loaded, "Concurrency Test Co (second)")).status(), "an edit carrying a stale version")
    .toBe(409);

  // A request naming no version at all is refused rather than waved through, so a caller that simply
  // forgets cannot quietly reinstate the old silent-overwrite behaviour.
  expect((await save(undefined, "Concurrency Test Co (no version)")).status(), "an edit naming no version")
    .toBe(400);

  const after = await page.request.get(`${API_URL}/api/customers`, { headers });
  const reloaded = (await after.json()).find((c: { id: number }) => c.id === customer.id);

  expect(reloaded.name, "the first writer's change is the one that survived")
    .toBe("Concurrency Test Co (first)");
  expect(reloaded.rowVersion, "and the version moved on exactly once").toBe(loaded + 1);
});
