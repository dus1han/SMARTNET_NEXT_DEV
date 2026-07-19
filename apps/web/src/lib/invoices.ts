import type {
  CreateInvoiceRequest,
  CreditStatus,
  DeletedInvoiceDetail,
  DeletedInvoiceSummary,
  EditInvoiceRequest,
  EmailDocumentRequest,
  EmailDocumentResponse,
  InvoiceCreatedResponse,
  InvoiceDeleted,
  InvoiceDetail,
  InvoiceEditedResponse,
  InvoiceRecipients,
  InvoiceSummary,
  InvoiceTaxRate,
} from "@smartnet/api-client";
import { api } from "./api";
import type { Paged } from "./paging";

// Generated from the API's OpenAPI schema — see packages/api-client. Re-exported, never redeclared.
export type {
  CreateInvoiceRequest,
  CreateInvoiceLineRequest,
  CreditStatus,
  DeletedInvoiceDetail,
  DeletedInvoiceSummary,
  EditInvoiceRequest,
  EditInvoiceLineRequest,
  InvoiceCreatedResponse,
  InvoiceDetail,
  InvoiceEditedResponse,
  InvoiceLineDetail,
  InvoiceSummary,
  InvoiceTaxRate,
} from "@smartnet/api-client";

/** The invoices this app has raised, newest first — outstanding derived from the ledger. */
/**
 * One page of invoices, searched and ordered by the server.
 *
 * The whole list used to come back — 2,485 rows, 342 KB — so the browser could page through it. The
 * server pages it now, which also means the search box searches every invoice rather than the page
 * that happens to be loaded.
 */
export const getInvoices = (params: { page: number; pageSize?: number; search?: string }) => {
  const query = new URLSearchParams({
    page: String(params.page),
    pageSize: String(params.pageSize ?? 25),
  });

  if (params.search?.trim()) query.set("search", params.search.trim());

  return api<Paged<InvoiceSummary>>(`/api/invoices?${query}`);
};

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

/**
 * Edit an issued invoice — versioned, reason-gated, concurrency-guarded. `reason` is mandatory. A stale
 * ExpectedRowVersion (someone else edited it) or a payment against the invoice comes back as a 409.
 */
export const editInvoice = (id: number, request: EditInvoiceRequest, reason: string) =>
  api<InvoiceEditedResponse>(`/api/invoices/${id}`, { method: "PUT", body: request, reason });

/** Void an invoice — soft, recoverable, reason-gated. A stale row_version is a 409. */
export const deleteInvoice = (id: number, expectedRowVersion: number, reason: string) =>
  api<InvoiceDeleted>(`/api/invoices/${id}?expectedRowVersion=${expectedRowVersion}`, { method: "DELETE", reason });

/** The deleted-invoice register — voided invoices, with who, when and why. */
export const getDeletedInvoices = () => api<DeletedInvoiceSummary[]>("/api/invoices/deleted");

/**
 * One deleted invoice in full — the detail behind a register row. Works for both a legacy deletion
 * (from del_invoice_h/l) and a new-app void, keyed by document number; carries who deleted it, when and
 * why for both.
 */
export const getDeletedInvoice = (number: string) =>
  api<DeletedInvoiceDetail>(`/api/invoices/deleted/${encodeURIComponent(number)}`);

/**
 * Who this invoice can be emailed to, and the covering message that would go with it.
 *
 * Works for a legacy invoice as readily as an adopted one — the contacts come from the customer's saved
 * records, resolved by the code the legacy document carries.
 */
export const invoiceRecipients = (id: number) =>
  api<InvoiceRecipients>(`/api/invoices/${id}/recipients`);

/**
 * Emails the invoice as a PDF attachment to the chosen saved contacts.
 *
 * Resolves 200 even when the mail server refused it — the response carries `sent` and the reason, so a
 * refusal is shown as a refusal, not as a failed request.
 */
export const emailInvoice = (id: number, request: EmailDocumentRequest) =>
  api<EmailDocumentResponse>(`/api/invoices/${id}/email`, { method: "POST", body: request });
