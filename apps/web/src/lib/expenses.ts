import type {
  CreateExpenseRequest,
  ExpenseCreatedResponse,
  ExpensePaymentRecordedResponse,
  ExpensePaymentSummary,
  ExpenseSummary,
  ExpenseCategoryDto,
  RecordExpensePaymentRequest,
  SaveExpenseCategoryRequest,
} from "@smartnet/api-client";
import { api } from "./api";

// Generated from the API's OpenAPI schema — see packages/api-client. Re-exported, never redeclared.
export type {
  CreateExpenseRequest,
  ExpenseCreatedResponse,
  ExpensePaymentRecordedResponse,
  ExpensePaymentSummary,
  ExpenseSummary,
  ExpenseCategoryDto,
  RecordExpensePaymentRequest,
  SaveExpenseCategoryRequest,
} from "@smartnet/api-client";

/** The expenses this app has recorded and the legacy ones adopted, newest first. */
export const getExpenses = () => api<ExpenseSummary[]>("/api/expenses");

/** Record an expense — what was incurred, unpaid; dual-writes the legacy row for the ExpenseReport. */
export const createExpense = (request: CreateExpenseRequest) =>
  api<ExpenseCreatedResponse>("/api/expenses", { method: "POST", body: request });

/** Every settlement against an expense, oldest first — including the ones backfilled at the migration. */
export const getExpensePayments = (id: number) => api<ExpensePaymentSummary[]>(`/api/expenses/${id}/payments`);

/** Settle an expense — all of what it still owes, or part of it. The outstanding comes back derived. */
export const recordExpensePayment = (id: number, request: RecordExpensePaymentRequest) =>
  api<ExpensePaymentRecordedResponse>(`/api/expenses/${id}/payments`, { method: "POST", body: request });

/** Void a settlement — soft, reason-gated; the expense goes back to owing that much. */
export const voidExpensePayment = (paymentId: number, expectedRowVersion: number, reason: string) =>
  api<void>(`/api/expenses/payments/${paymentId}?expectedRowVersion=${expectedRowVersion}`, { method: "DELETE", reason });

/** Void an expense — soft, reason-gated. A stale row_version is a 409. */
export const voidExpense = (id: number, expectedRowVersion: number, reason: string) =>
  api<void>(`/api/expenses/${id}?expectedRowVersion=${expectedRowVersion}`, { method: "DELETE", reason });

/** Every expense category (shared across companies). */
export const getExpenseCategories = () => api<ExpenseCategoryDto[]>("/api/expenses/categories");

/** Add a category. */
export const addExpenseCategory = (request: SaveExpenseCategoryRequest) =>
  api<ExpenseCategoryDto>("/api/expenses/categories", { method: "POST", body: request });

/** Rename a category. */
export const renameExpenseCategory = (id: number, request: SaveExpenseCategoryRequest) =>
  api<void>(`/api/expenses/categories/${id}`, { method: "PUT", body: request });
