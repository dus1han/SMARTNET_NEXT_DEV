import type {
  ChequeReportResponse,
  CompanyOption,
  CustomerSalesResponse,
  CustomerVatResponse,
  DunningResponse,
  ExpenseCategory,
  ExpenseReportResponse,
  JobCardReportResponse,
  OutstandingResponse,
  SalesReportResponse,
  StatementRecipients,
  SupplierOption,
  SupplierPaymentResponse,
  ProfitLossResponse,
  DataExceptionsResponse,
  ResolveDataExceptionRequest,
  SupplierPurchaseResponse,
  SupplierVatResponse,
  TrialBalanceResponse,
} from "@smartnet/api-client";
import { api } from "./api";

// Generated from the API's OpenAPI schema — see packages/api-client. Re-exported, never redeclared.
export type {
  ChequeReportResponse,
  ChequeRow,
  CompanyOption,
  CustomerSalesResponse,
  CustomerSalesRow,
  CustomerVatResponse,
  CustomerVatRow,
  ExpenseCategory,
  ExpenseReportResponse,
  ExpenseReportRow,
  JobCardReportResponse,
  JobCardRow,
  OutstandingResponse,
  OutstandingRow,
  SalesReportResponse,
  SalesReportRow,
  SalesReportSummary,
  SupplierOption,
  SupplierPaymentResponse,
  SupplierPaymentRow,
  SupplierPurchaseResponse,
  SupplierPurchaseRow,
  SupplierVatResponse,
  SupplierVatRow,
  TrialBalanceResponse,
  TrialBalanceRow,
  ProfitLossResponse,
  ProfitLossLine,
  ProfitLossReconciliation,
  DataExceptionsResponse,
  DataExceptionRow,
  ResolveDataExceptionRequest,
} from "@smartnet/api-client";

/** The company a report is scoped to: a specific company id, or "all" (every accessible one). */
export type CompanyFilter = number | "all";

/** The query value for a company filter — omitted for "all", the id otherwise. */
const companyParam = (company: CompanyFilter) => (company === "all" ? undefined : String(company));

/** The companies the user may filter any report by — the shared All/company selector's options. */
export const getReportCompanies = () => api<CompanyOption[]>("/api/reports/companies");

/**
 * The date window a report covers — ISO `yyyy-MM-dd` strings, matching the API. Both ends are
 * optional; an omitted end is unbounded on that side. The company is NOT here: reports scope to the
 * company the user is signed into (the shell switcher), exactly like every other screen.
 */
export interface ReportPeriod {
  from?: string;
  to?: string;
}

/** Builds the query string the reports share, dropping anything unset. */
function periodQuery(period: ReportPeriod, extra: Record<string, string | undefined> = {}): string {
  const params = new URLSearchParams();

  if (period.from) params.set("from", period.from);
  if (period.to) params.set("to", period.to);

  for (const [key, value] of Object.entries(extra)) {
    if (value) params.set(key, value);
  }

  const query = params.toString();
  return query ? `?${query}` : "";
}

// --- Sales (sales_rpt) -----------------------------------------------------------------------

export const getSalesReport = (period: ReportPeriod, company: CompanyFilter) =>
  api<SalesReportResponse>(`/api/reports/sales${periodQuery(period, { company: companyParam(company) })}`);

/** The export is a server-generated .xlsx; the path (with filters) is handed to downloadExcel. */
export const salesReportExportUrl = (period: ReportPeriod, company: CompanyFilter) =>
  `/api/reports/sales/export${periodQuery(period, { company: companyParam(company) })}`;

// --- Expenses (expenses_rpt) -----------------------------------------------------------------

export const getExpenseReport = (period: ReportPeriod, company: CompanyFilter, category?: number) =>
  api<ExpenseReportResponse>(
    `/api/reports/expenses${periodQuery(period, { company: companyParam(company), category: category?.toString() })}`,
  );

export const expenseReportExportUrl = (period: ReportPeriod, company: CompanyFilter, category?: number) =>
  `/api/reports/expenses/export${periodQuery(period, { company: companyParam(company), category: category?.toString() })}`;

export const getExpenseCategories = () =>
  api<ExpenseCategory[]>("/api/reports/expenses/categories");

// --- Customer sales (customersales_rpt) ------------------------------------------------------

export const getCustomerSalesReport = (period: ReportPeriod, company: CompanyFilter) =>
  api<CustomerSalesResponse>(`/api/reports/customer-sales${periodQuery(period, { company: companyParam(company) })}`);

export const customerSalesReportExportUrl = (period: ReportPeriod, company: CompanyFilter) =>
  `/api/reports/customer-sales/export${periodQuery(period, { company: companyParam(company) })}`;

// --- Cheques (chequerpt) ---------------------------------------------------------------------

export const getChequeReport = (period: ReportPeriod, company: CompanyFilter) =>
  api<ChequeReportResponse>(`/api/reports/cheques${periodQuery(period, { company: companyParam(company) })}`);

export const chequeReportExportUrl = (period: ReportPeriod, company: CompanyFilter) =>
  `/api/reports/cheques/export${periodQuery(period, { company: companyParam(company) })}`;

// --- Job cards (jobcards_rpt) ----------------------------------------------------------------

export const getJobCardReport = (period: ReportPeriod, company: CompanyFilter) =>
  api<JobCardReportResponse>(`/api/reports/job-cards${periodQuery(period, { company: companyParam(company) })}`);

export const jobCardReportExportUrl = (period: ReportPeriod, company: CompanyFilter) =>
  `/api/reports/job-cards/export${periodQuery(period, { company: companyParam(company) })}`;

// --- Customer VAT (cusvat_rpt) ---------------------------------------------------------------

export const getCustomerVatReport = (period: ReportPeriod, company: CompanyFilter) =>
  api<CustomerVatResponse>(`/api/reports/customer-vat${periodQuery(period, { company: companyParam(company) })}`);

export const customerVatReportExportUrl = (period: ReportPeriod, company: CompanyFilter) =>
  `/api/reports/customer-vat/export${periodQuery(period, { company: companyParam(company) })}`;

// --- Supplier VAT (suppliervat_rpt) ----------------------------------------------------------

export const getSupplierVatReport = (period: ReportPeriod, company: CompanyFilter) =>
  api<SupplierVatResponse>(`/api/reports/supplier-vat${periodQuery(period, { company: companyParam(company) })}`);

export const supplierVatReportExportUrl = (period: ReportPeriod, company: CompanyFilter) =>
  `/api/reports/supplier-vat/export${periodQuery(period, { company: companyParam(company) })}`;

// --- Trial balance (general ledger) ----------------------------------------------------------

export const getTrialBalanceReport = (period: ReportPeriod, company: CompanyFilter) =>
  api<TrialBalanceResponse>(`/api/reports/trial-balance${periodQuery(period, { company: companyParam(company) })}`);

export const trialBalanceReportExportUrl = (period: ReportPeriod, company: CompanyFilter) =>
  `/api/reports/trial-balance/export${periodQuery(period, { company: companyParam(company) })}`;

// --- Data exceptions (LEGACY-DATA-POLICY §4) -------------------------------------------------

export const getDataExceptions = (company: CompanyFilter) =>
  api<DataExceptionsResponse>(`/api/reports/data-exceptions${periodQuery({}, { company: companyParam(company) })}`);

export const dataExceptionsExportUrl = (company: CompanyFilter) =>
  `/api/reports/data-exceptions/export${periodQuery({}, { company: companyParam(company) })}`;

/** Applies an audited correction to one data exception; the exception then self-clears from the list. */
export const resolveDataException = (body: ResolveDataExceptionRequest) =>
  api<void>("/api/reports/data-exceptions/resolve", { method: "POST", body });

// --- Profit & loss (general ledger) ----------------------------------------------------------

export const getProfitLossReport = (period: ReportPeriod, company: CompanyFilter) =>
  api<ProfitLossResponse>(`/api/reports/profit-loss${periodQuery(period, { company: companyParam(company) })}`);

export const profitLossReportExportUrl = (period: ReportPeriod, company: CompanyFilter) =>
  `/api/reports/profit-loss/export${periodQuery(period, { company: companyParam(company) })}`;

// --- Supplier purchase (supplierpurchase_rpt) ------------------------------------------------

export const getSupplierPurchaseReport = (period: ReportPeriod, company: CompanyFilter) =>
  api<SupplierPurchaseResponse>(`/api/reports/supplier-purchase${periodQuery(period, { company: companyParam(company) })}`);

export const supplierPurchaseReportExportUrl = (period: ReportPeriod, company: CompanyFilter) =>
  `/api/reports/supplier-purchase/export${periodQuery(period, { company: companyParam(company) })}`;

// --- Supplier payments (supplierpayments_rpt) ------------------------------------------------

export const getSupplierPaymentReport = (period: ReportPeriod, company: CompanyFilter, supplier?: string) =>
  api<SupplierPaymentResponse>(
    `/api/reports/supplier-payments${periodQuery(period, { company: companyParam(company), supplier })}`,
  );

export const supplierPaymentReportExportUrl = (period: ReportPeriod, company: CompanyFilter, supplier?: string) =>
  `/api/reports/supplier-payments/export${periodQuery(period, { company: companyParam(company), supplier })}`;

export const getReportSuppliers = () => api<SupplierOption[]>("/api/reports/suppliers");

// --- Customer outstanding (customer_outstanding) ---------------------------------------------
// Point-in-time — the company filter and an "as at" date. The date defaults to today (the live figure);
// a past date rolls the balances back to what was owed then. `asAt` is a YYYY-MM-DD string, or undefined
// for today.

export const getOutstandingReport = (company: CompanyFilter, asAt?: string) =>
  api<OutstandingResponse>(
    `/api/reports/outstanding${periodQuery({}, { company: companyParam(company), asAt })}`,
  );

export const outstandingReportExportUrl = (company: CompanyFilter, asAt?: string) =>
  `/api/reports/outstanding/export${periodQuery({}, { company: companyParam(company), asAt })}`;

/** The per-invoice outstanding list for the selected customers — the "export selected" sheet. */
export const outstandingDetailExportUrl = (company: CompanyFilter, customers: string[], asAt?: string) =>
  `/api/reports/outstanding/detail/export${periodQuery(
    {},
    { company: companyParam(company), customers: customers.join(",") || undefined, asAt },
  )}`;

// --- Bulk dunning (the one write) ------------------------------------------------------------

export type { DunningResponse, StatementRecipients } from "@smartnet/api-client";

/**
 * Queues an outstanding statement to each selected customer. Returns at once. Whether anything is
 * actually sent is gated by the company's mail kill switch (off by default) — the response says which.
 *
 * `contactIds` names which of the customer's saved contacts to send to. It applies to a single
 * customer only: a bulk run spans many customers with different contact lists, so there is no one
 * list to choose from and each falls back to its own address on file. The server enforces the same.
 */
export const sendDunning = (customers: string[], contactIds?: number[]) =>
  api<DunningResponse>("/api/dunning/outstanding", {
    method: "POST",
    body: { customers, contactIds: contactIds ?? null },
  });

/** Who one customer's statement can go to — their saved contacts, and why sending might be blocked. */
export const statementRecipients = (customerCode: string) =>
  api<StatementRecipients>(`/api/dunning/outstanding/${encodeURIComponent(customerCode)}/recipients`);
