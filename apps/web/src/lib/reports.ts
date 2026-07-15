import type {
  ChequeReportResponse,
  CustomerSalesResponse,
  CustomerVatResponse,
  ExpenseCategory,
  ExpenseReportResponse,
  JobCardReportResponse,
  SalesReportResponse,
  SupplierOption,
  SupplierPaymentResponse,
  SupplierPurchaseResponse,
  SupplierVatResponse,
} from "@smartnet/api-client";
import { api } from "./api";

// Generated from the API's OpenAPI schema — see packages/api-client. Re-exported, never redeclared.
export type {
  ChequeReportResponse,
  ChequeRow,
  CustomerSalesResponse,
  CustomerSalesRow,
  CustomerVatResponse,
  CustomerVatRow,
  ExpenseCategory,
  ExpenseReportResponse,
  ExpenseReportRow,
  JobCardReportResponse,
  JobCardRow,
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
} from "@smartnet/api-client";

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

export const getSalesReport = (period: ReportPeriod) =>
  api<SalesReportResponse>(`/api/reports/sales${periodQuery(period)}`);

/** The export is a server-generated .xlsx; the path (with filters) is handed to downloadExcel. */
export const salesReportExportUrl = (period: ReportPeriod) =>
  `/api/reports/sales/export${periodQuery(period)}`;

// --- Expenses (expenses_rpt) -----------------------------------------------------------------

export const getExpenseReport = (period: ReportPeriod, category?: number) =>
  api<ExpenseReportResponse>(
    `/api/reports/expenses${periodQuery(period, { category: category?.toString() })}`,
  );

export const expenseReportExportUrl = (period: ReportPeriod, category?: number) =>
  `/api/reports/expenses/export${periodQuery(period, { category: category?.toString() })}`;

export const getExpenseCategories = () =>
  api<ExpenseCategory[]>("/api/reports/expenses/categories");

// --- Customer sales (customersales_rpt) ------------------------------------------------------

export const getCustomerSalesReport = (period: ReportPeriod) =>
  api<CustomerSalesResponse>(`/api/reports/customer-sales${periodQuery(period)}`);

export const customerSalesReportExportUrl = (period: ReportPeriod) =>
  `/api/reports/customer-sales/export${periodQuery(period)}`;

// --- Cheques (chequerpt) ---------------------------------------------------------------------

export const getChequeReport = (period: ReportPeriod) =>
  api<ChequeReportResponse>(`/api/reports/cheques${periodQuery(period)}`);

export const chequeReportExportUrl = (period: ReportPeriod) =>
  `/api/reports/cheques/export${periodQuery(period)}`;

// --- Job cards (jobcards_rpt) ----------------------------------------------------------------

export const getJobCardReport = (period: ReportPeriod) =>
  api<JobCardReportResponse>(`/api/reports/job-cards${periodQuery(period)}`);

export const jobCardReportExportUrl = (period: ReportPeriod) =>
  `/api/reports/job-cards/export${periodQuery(period)}`;

// --- Customer VAT (cusvat_rpt) ---------------------------------------------------------------

export const getCustomerVatReport = (period: ReportPeriod) =>
  api<CustomerVatResponse>(`/api/reports/customer-vat${periodQuery(period)}`);

export const customerVatReportExportUrl = (period: ReportPeriod) =>
  `/api/reports/customer-vat/export${periodQuery(period)}`;

// --- Supplier VAT (suppliervat_rpt) ----------------------------------------------------------

export const getSupplierVatReport = (period: ReportPeriod) =>
  api<SupplierVatResponse>(`/api/reports/supplier-vat${periodQuery(period)}`);

export const supplierVatReportExportUrl = (period: ReportPeriod) =>
  `/api/reports/supplier-vat/export${periodQuery(period)}`;

// --- Supplier purchase (supplierpurchase_rpt) ------------------------------------------------

export const getSupplierPurchaseReport = (period: ReportPeriod) =>
  api<SupplierPurchaseResponse>(`/api/reports/supplier-purchase${periodQuery(period)}`);

export const supplierPurchaseReportExportUrl = (period: ReportPeriod) =>
  `/api/reports/supplier-purchase/export${periodQuery(period)}`;

// --- Supplier payments (supplierpayments_rpt) ------------------------------------------------

export const getSupplierPaymentReport = (period: ReportPeriod, supplier?: string) =>
  api<SupplierPaymentResponse>(`/api/reports/supplier-payments${periodQuery(period, { supplier })}`);

export const supplierPaymentReportExportUrl = (period: ReportPeriod, supplier?: string) =>
  `/api/reports/supplier-payments/export${periodQuery(period, { supplier })}`;

export const getReportSuppliers = () => api<SupplierOption[]>("/api/reports/suppliers");
