import type { DashboardResponse, DashboardAnalytics, CustomerInsight, OperationsDashboard } from "@smartnet/api-client";
import { api } from "./api";

// Generated from the API's OpenAPI schema — see packages/api-client. Re-exported, never redeclared.
export type { DashboardResponse, DailySalesPoint, DashboardCompanyOption, DashboardAnalytics, MonthPoint, CashFlowPoint, AgeingBucket, CustomerShare, SalesMix, CustomerDebt, SupplierShare, CreditBreach, LapsedCustomer, CustomerInsight, CustomerInvoiceRow, CustomerPaymentRow, OperationsDashboard, RecentDocument, Trend } from "@smartnet/api-client";

/** The company the dashboard is scoped to: a specific company id, or "all" (every accessible one). */
export type CompanyFilter = number | "all";

/**
 * The dashboard. The server chooses the shape from the token — a caller who holds `dashboard` gets the
 * company view, everyone else the "my" view scoped to what they prepared — and scopes the figures to
 * the chosen company, or aggregates every company the caller may see when `all`. The server only ever
 * honours a company the token permits, so this can only narrow to something already allowed.
 */
export const getDashboard = (company: CompanyFilter = "all") =>
  api<DashboardResponse>(`/api/dashboard${company === "all" ? "" : `?company=${company}`}`);

/**
 * The analytical half of the dashboard — trend, ageing, cash movement, concentration.
 *
 * A second request rather than part of `getDashboard`: it scans a year of invoices and their lines, so
 * the month tiles paint immediately and this fills in behind them.
 */
export const getDashboardAnalytics = (company: CompanyFilter) =>
  api<DashboardAnalytics>(`/api/dashboard/analytics${company === "all" ? "" : `?company=${company}`}`);

/**
 * One customer's whole account — the drill-down every dashboard panel links to.
 *
 * Keyed by the legacy customer code, which is what the documents carry and what the panels hold.
 */
export const getCustomerInsight = (code: string) =>
  api<CustomerInsight>(`/api/dashboard/customer/${encodeURIComponent(code)}`);

/**
 * The operations dashboard — the day-to-day view, for users without the management one.
 *
 * A separate endpoint rather than a filtered response: the figures a normal user must not see are never
 * sent, so they cannot be read off the network tab.
 */
export const getOperationsDashboard = (company: CompanyFilter) =>
  api<OperationsDashboard>(`/api/dashboard/operations${company === "all" ? "" : `?company=${company}`}`);
