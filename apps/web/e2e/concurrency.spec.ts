import { test, expect, type APIResponse, type Page } from "@playwright/test";
import { API_URL, SEED } from "./seed";

/**
 * Every write that now demands a version, swept in one place.
 *
 * These shipped verified only by a typecheck and a reading of the code. They are all *breaking* changes
 * — a screen that forgets to send a version is refused, not merely unprotected — so "it compiles" is
 * not evidence that the screen still saves.
 *
 * Each case saves once with the version it read, which must succeed, then again with the same, now
 * stale, version, which must be refused.
 *
 * <b>Each first save has to change something real.</b> The audit interceptor counts an update only when
 * a value actually differs, so writing a record back unchanged does not move its version and the second
 * save then legitimately succeeds. That is correct behaviour, and worth knowing: a no-op save is not a
 * version bump. The fields chosen below are ones no other spec reads.
 */

async function login(page: Page) {
  await page.goto("/login");
  await page.locator('input[name="username"]').fill(SEED.username);
  await page.locator('input[name="password"]').fill(SEED.password);
  await page.locator('input[name="password"]').press("Enter");
  await page.waitForURL((url) => url.pathname === "/");
}

async function companyId(page: Page): Promise<number> {
  const response = await page.request.get(`${API_URL}/api/companies`);
  expect(response.ok(), "listing companies").toBeTruthy();

  const company = (await response.json()).find((c: { name: string }) => c.name === SEED.company);
  expect(company, "the seeded company").toBeTruthy();

  return company.id as number;
}

/** Asserts a status, and puts the server's own words in the message when it disagrees. */
async function expectStatus(response: APIResponse, status: number, what: string) {
  expect(response.status(), `${what} — server said: ${await response.text()}`).toBe(status);
}

test.beforeEach(async ({ page }) => {
  await login(page);
});

test("master data and settings accept a current version and refuse a stale one", async ({ page }) => {
  const headers = { "X-Company-Id": String(await companyId(page)) };
  const audited = { ...headers, "X-Change-Reason": "e2e concurrency check" };

  const get = async (path: string) => {
    const response = await page.request.get(`${API_URL}${path}`, { headers });
    expect(response.ok(), `GET ${path}`).toBeTruthy();
    return response.json();
  };

  // --- Supplier ---------------------------------------------------------------------------------
  const supplier = (await get("/api/suppliers")).find((s: { code: string }) => s.code === "E2E-SUP");
  expect(supplier, "the seeded supplier").toBeTruthy();

  // The address, not the name — the other specs find this supplier by name.
  const saveSupplier = (rowVersion: number, address: string) =>
    page.request.put(`${API_URL}/api/suppliers/${supplier.id}`, {
      headers,
      data: { name: supplier.name, address, expectedRowVersion: rowVersion },
    });

  await expectStatus(await saveSupplier(supplier.rowVersion, "1 Concurrency Way"), 204, "supplier, current");
  await expectStatus(await saveSupplier(supplier.rowVersion, "2 Concurrency Way"), 409, "supplier, stale");

  // --- Item -------------------------------------------------------------------------------------
  const item = (await get("/api/items")).find((i: { code: string }) => i.code === "E2E-ITEM");
  expect(item, "the seeded item").toBeTruthy();

  // The reorder level, not the price — the invoice and quotation specs assert on the price.
  const saveItem = (rowVersion: number, reorderLevel: number) =>
    page.request.put(`${API_URL}/api/items/${item.id}`, {
      headers,
      data: {
        name: item.name,
        sellingPrice: item.sellingPrice,
        cost: item.cost,
        reorderLevel,
        unit: item.unit,
        expectedRowVersion: rowVersion,
      },
    });

  await expectStatus(await saveItem(item.rowVersion, 5), 204, "item, current");
  await expectStatus(await saveItem(item.rowVersion, 6), 409, "item, stale");

  // --- Company profile --------------------------------------------------------------------------
  // The read shape is the write shape, so the version comes back on the field it was read from.
  const profile = await get("/api/settings/company");

  const saveCompany = (rowVersion: number, website: string) =>
    page.request.put(`${API_URL}/api/settings/company`, {
      headers: audited,
      data: { ...profile, website, rowVersion },
    });

  await expectStatus(await saveCompany(profile.rowVersion, "https://one.example"), 200, "company, current");
  await expectStatus(await saveCompany(profile.rowVersion, "https://two.example"), 409, "company, stale");

  // --- Numbering series -------------------------------------------------------------------------
  // The quotation series specifically. Prefix and padding decide what documents are called, and the
  // invoice, PO and job-card series are the ones the other specs raise against.
  const series = (await get("/api/settings/numbering"))
    .find((s: { docType: string }) => s.docType === "QUOTATION");
  expect(series, "the quotation numbering series").toBeTruthy();

  const saveSeries = (rowVersion: number, padding: number) =>
    page.request.put(`${API_URL}/api/settings/numbering/${series.id}`, {
      headers: audited,
      data: { prefix: series.prefix, padding, expectedRowVersion: rowVersion },
    });

  await expectStatus(await saveSeries(series.rowVersion, 4), 204, "numbering, current");
  await expectStatus(await saveSeries(series.rowVersion, 5), 409, "numbering, stale");

  // --- Business rules ---------------------------------------------------------------------------
  // Per rule, not per screen: the rows have that grain, so two people editing different rules must not
  // conflict at all. A rule at version 0 has no row of this company's own — the value shown came from
  // the global setting or the built-in default — so saving it creates one.
  //
  // Saved at its current value, so nothing about how the app behaves changes: what is under test is the
  // version moving from 0 to 1. Changing a real rule here would quietly alter credit-limit enforcement
  // underneath the invoice specs.
  const rules = await get("/api/settings/business-rules");
  const rule = rules.find((r: { rowVersion: number }) => r.rowVersion === 0);
  expect(rule, "a business rule this company has no row of its own for").toBeTruthy();

  const saveRule = (rowVersion: number) =>
    page.request.put(`${API_URL}/api/settings/business-rules`, {
      headers: audited,
      data: [{ key: rule.key, value: rule.value, rowVersion }],
    });

  await expectStatus(await saveRule(0), 204, "business rule, no row of our own yet");
  await expectStatus(await saveRule(0), 409, "business rule, stale (a row exists now)");
});

/**
 * A user's permissions, which had no version of their own until now.
 *
 * The endpoint writes `user_permission_overrides` and never touches `user_m`, so the user's row_version
 * did not move when their access changed. A check against it would have passed straight through a
 * concurrent edit — reading as protection while providing none, which is worse than no check at all.
 * The permission write now moves the user's version deliberately (`TouchForConcurrency`); this proves it.
 */
test("a permission change moves the user's version, so the next stale one is refused", async ({ page }) => {
  const headers = {
    "X-Company-Id": String(await companyId(page)),
    "X-Change-Reason": "e2e concurrency check",
  };

  const users = await page.request.get(`${API_URL}/api/users`, { headers });
  expect(users.ok()).toBeTruthy();

  const user = (await users.json()).find((u: { username: string }) => u.username === SEED.username);
  expect(user, "the seeded user").toBeTruthy();

  // Written back exactly as read, so the account this whole suite signs in with keeps its access. The
  // version still moves, because the write itself touches the user — which is the point.
  const permissions = user.effectivePermissions as string[];

  const save = (rowVersion: number) =>
    page.request.put(`${API_URL}/api/users/${user.id}/permissions`, {
      headers,
      data: { permissions, expectedRowVersion: rowVersion },
    });

  const loaded = user.rowVersion as number;

  await expectStatus(await save(loaded), 204, "permissions, current version");

  // The heart of it. Had the write not touched the user, the version would still be `loaded` here and
  // this would succeed — silently reinstating whatever another administrator had just revoked.
  await expectStatus(await save(loaded), 409, "permissions, stale version");

  const after = await page.request.get(`${API_URL}/api/users`, { headers });
  const reloaded = (await after.json()).find((u: { id: number }) => u.id === user.id);

  expect(reloaded.rowVersion, "the permission change moved the user's version").toBeGreaterThan(loaded);
  expect([...reloaded.effectivePermissions].sort(), "and took nothing away")
    .toEqual([...permissions].sort());
});
