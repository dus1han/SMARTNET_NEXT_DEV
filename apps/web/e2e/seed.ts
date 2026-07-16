/**
 * The seed identifiers the E2E host (`tools/E2EHost`) creates, shared with the specs. Kept in one
 * place so a change to the seed is a change in one file, not a hunt through selectors.
 */
export const SEED = {
  username: "e2e",
  password: "E2Epassw0rd!",
  company: "E2E Trading Co",
  customer: "E2E Customer",
  item: "E2E Widget",
  itemPrice: 100,
  supplier: "E2E Supplier",
} as const;

export const API_URL = process.env.E2E_API_URL ?? "http://localhost:5099";
export const WEB_URL = process.env.E2E_WEB_URL ?? "http://localhost:3100";
