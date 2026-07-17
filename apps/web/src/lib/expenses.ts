import type {
  CreateExpenseRequest,
  ExpenseCreatedResponse,
  ExpenseSummary,
  ExpenseCategoryDto,
  SaveExpenseCategoryRequest,
} from "@smartnet/api-client";
import { api } from "./api";

// Generated from the API's OpenAPI schema — see packages/api-client. Re-exported, never redeclared.
export type {
  CreateExpenseRequest,
  ExpenseCreatedResponse,
  ExpenseSummary,
  ExpenseCategoryDto,
  SaveExpenseCategoryRequest,
} from "@smartnet/api-client";

/** The expenses this app has recorded and the legacy ones adopted, newest first. */
export const getExpenses = () => api<ExpenseSummary[]>("/api/expenses");

/** Record an expense — a flat log entry; dual-writes the legacy row for the ExpenseReport. */
export const createExpense = (request: CreateExpenseRequest) =>
  api<ExpenseCreatedResponse>("/api/expenses", { method: "POST", body: request });

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
