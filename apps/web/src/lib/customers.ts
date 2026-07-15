import type {
  CompanySummary,
  CreateCustomerResponse,
  CustomerSummary,
  ProfitPercent,
  SaveCustomerRequest,
} from "@smartnet/api-client";
import { api } from "./api";

// Generated from the API's OpenAPI schema — see packages/api-client. Re-exported, never redeclared.
export type { CompanySummary, CustomerSummary, ProfitPercent, SaveCustomerRequest };

export const listCustomers = () => api<CustomerSummary[]>("/api/customers");

/** The margin bands for the form's dropdown — 5%, 10%, … from profit_percent. */
export const listProfitPercents = () => api<ProfitPercent[]>("/api/customers/profit-percents");

/** The trading entities, for the "associated with" field. Shared endpoint with the company switcher. */
export const listCompanies = () => api<CompanySummary[]>("/api/companies");

/**
 * Creates a customer. The code — "C-232" — is allocated by the server from the same sequence the
 * legacy app uses, and comes back in the response; the client never invents it.
 */
export const createCustomer = (customer: SaveCustomerRequest) =>
  api<CreateCustomerResponse>("/api/customers", { method: "POST", body: customer });

export const updateCustomer = (id: number, customer: SaveCustomerRequest) =>
  api<void>(`/api/customers/${id}`, { method: "PUT", body: customer });

/** `reason` is mandatory. The server rejects a delete that does not explain itself. */
export const deleteCustomer = (id: number, reason: string) =>
  api<void>(`/api/customers/${id}`, { method: "DELETE", reason });
