import type {
  CreateInvoiceRequest,
  CreditStatus,
  InvoiceCreatedResponse,
  InvoiceDetail,
  InvoiceSummary,
  InvoiceTaxRate,
} from "@smartnet/api-client";
import { api } from "./api";

// Generated from the API's OpenAPI schema — see packages/api-client. Re-exported, never redeclared.
export type {
  CreateInvoiceRequest,
  CreateInvoiceLineRequest,
  CreditStatus,
  InvoiceCreatedResponse,
  InvoiceDetail,
  InvoiceLineDetail,
  InvoiceSummary,
  InvoiceTaxRate,
} from "@smartnet/api-client";

/** The invoices this app has raised, newest first — outstanding derived from the ledger. */
export const getInvoices = () => api<InvoiceSummary[]>("/api/invoices");

/** One invoice in full, with its lines. */
export const getInvoice = (id: number) => api<InvoiceDetail>(`/api/invoices/${id}`);

/**
 * The one VAT rate a document raised for this company on this date will carry — resolved by the same
 * server-side engine the save uses, so the screen's live preview matches the total it will be charged.
 * Fetched once when the company or date changes, never per line: nothing is sent while the user types.
 */
export const getInvoiceTaxRate = (companyId: number, date: string) =>
  api<InvoiceTaxRate>(`/api/invoices/tax-rate?companyId=${companyId}&date=${date}`);

/**
 * A customer's credit standing — outstanding (the derived ledger balance), their limit, and whether the
 * company hard-blocks a breach. The screen shows this when a customer is picked, and confirms before a
 * save that would breach it — the same figures the server-side gate uses.
 */
export const getCreditStatus = (customerId: number, companyId: number) =>
  api<CreditStatus>(`/api/invoices/credit-status?customerId=${customerId}&companyId=${companyId}`);

/** Raise an invoice — the whole document, posted once (the cart is gone). */
export const createInvoice = (request: CreateInvoiceRequest) =>
  api<InvoiceCreatedResponse>("/api/invoices", { method: "POST", body: request });
