/**
 * The API's types, generated from its OpenAPI schema.
 *
 * This file is the only hand-written thing in the package, and it does exactly one thing: give the
 * generated types names a screen can import. `schema.d.ts` is machine output and is not edited.
 *
 * If a name below stops compiling, the API changed. That is the entire point — a hand-written
 * `interface UserSummary` would have gone on compiling, and gone on being wrong.
 */
import type { components, paths } from "./schema";

type Schemas = components["schemas"];

// --- Auth ------------------------------------------------------------------------------------

export type LoginRequest = Schemas["LoginRequest"];
export type LoginResponse = Schemas["LoginResponse"];
export type Me = Schemas["MeResponse"];
export type ChangePasswordRequest = Schemas["ChangePasswordRequest"];

// --- Users & roles ---------------------------------------------------------------------------

export type UserSummary = Schemas["UserSummary"];
export type CreateUserRequest = Schemas["CreateUserRequest"];
export type CreateUserResponse = Schemas["CreateUserResponse"];
export type UpdateUserRequest = Schemas["UpdateUserRequest"];
export type ResetPasswordResponse = Schemas["ResetPasswordResponse"];
export type SetOverrideRequest = Schemas["SetOverrideRequest"];

export type RoleSummary = Schemas["RoleSummary"];
export type SaveRoleRequest = Schemas["SaveRoleRequest"];
export type PermissionCatalogueEntry = Schemas["PermissionCatalogueEntry"];

// --- Settings --------------------------------------------------------------------------------

export type CompanySummary = Schemas["CompanySummary"];
export type CompanyProfile = Schemas["CompanyProfile"];
export type BusinessRule = Schemas["BusinessRule"];
export type TaxRate = Schemas["TaxRateDto"];
export type SaveTaxRateRequest = Schemas["SaveTaxRateRequest"];
export type MailSettings = Schemas["MailSettingsResponse"];
export type SaveMailSettingsRequest = Schemas["SaveMailSettingsRequest"];
export type EmailTemplate = Schemas["EmailTemplateDto"];

// --- Document numbering ----------------------------------------------------------------------

export type DocumentSeries = Schemas["DocumentSeriesDto"];
export type SaveDocumentSeriesRequest = Schemas["SaveDocumentSeriesRequest"];
export type SeriesInitialisation = Schemas["SeriesInitialisation"];
export type NumberPreview = Schemas["NumberPreview"];

// --- Master data -----------------------------------------------------------------------------

export type CustomerSummary = Schemas["CustomerSummary"];
export type SaveCustomerRequest = Schemas["SaveCustomerRequest"];
export type CreateCustomerResponse = Schemas["CreateCustomerResponse"];
export type ProfitPercent = Schemas["ProfitPercentDto"];

export type SupplierSummary = Schemas["SupplierSummary"];
export type SaveSupplierRequest = Schemas["SaveSupplierRequest"];
export type CreateSupplierResponse = Schemas["CreateSupplierResponse"];

export type ItemSummary = Schemas["ItemSummary"];
export type SaveItemRequest = Schemas["SaveItemRequest"];
export type CreateItemResponse = Schemas["CreateItemResponse"];
export type ItemStock = Schemas["ItemStockResponse"];
export type StockMovement = Schemas["StockMovementDto"];
export type StockBatch = Schemas["StockBatchDto"];
export type CreateStockAdjustmentRequest = Schemas["CreateStockAdjustmentRequest"];

// --- Dashboard -------------------------------------------------------------------------------

export type DashboardResponse = Schemas["DashboardResponse"];
export type DailySalesPoint = Schemas["DailySalesPoint"];
export type DashboardCompanyOption = Schemas["DashboardCompanyOption"];

// --- Reports ---------------------------------------------------------------------------------

export type SalesReportResponse = Schemas["SalesReportResponse"];
export type SalesReportRow = Schemas["SalesReportRow"];
export type SalesReportSummary = Schemas["SalesReportSummary"];
export type ExpenseReportResponse = Schemas["ExpenseReportResponse"];
export type ExpenseReportRow = Schemas["ExpenseReportRow"];
export type ExpenseCategory = Schemas["ExpenseCategoryDto"];

export type CustomerSalesResponse = Schemas["CustomerSalesResponse"];
export type CustomerSalesRow = Schemas["CustomerSalesRow"];
export type ChequeReportResponse = Schemas["ChequeReportResponse"];
export type ChequeRow = Schemas["ChequeRow"];
export type JobCardReportResponse = Schemas["JobCardReportResponse"];
export type JobCardRow = Schemas["JobCardRow"];

export type CustomerVatResponse = Schemas["CustomerVatResponse"];
export type CustomerVatRow = Schemas["CustomerVatRow"];
export type SupplierVatResponse = Schemas["SupplierVatResponse"];
export type SupplierVatRow = Schemas["SupplierVatRow"];
export type SupplierPurchaseResponse = Schemas["SupplierPurchaseResponse"];
export type SupplierPurchaseRow = Schemas["SupplierPurchaseRow"];
export type SupplierPaymentResponse = Schemas["SupplierPaymentResponse"];
export type SupplierPaymentRow = Schemas["SupplierPaymentRow"];
export type SupplierOption = Schemas["SupplierOption"];
export type CompanyOption = Schemas["CompanyOption"];
export type OutstandingResponse = Schemas["OutstandingResponse"];
export type OutstandingRow = Schemas["OutstandingRow"];
export type DunningResponse = Schemas["DunningResponse"];

// --- Documents (Phase 5) ---------------------------------------------------------------------

export type CreateInvoiceRequest = Schemas["CreateInvoiceRequest"];
export type CreateInvoiceLineRequest = Schemas["CreateInvoiceLineRequest"];
export type InvoiceCreatedResponse = Schemas["InvoiceCreatedResponse"];
export type InvoiceTaxRate = Schemas["InvoiceTaxRate"];
export type CreditStatus = Schemas["CreditStatus"];
export type InvoiceSummary = Schemas["InvoiceSummary"];
export type InvoiceDetail = Schemas["InvoiceDetail"];
export type InvoiceLineDetail = Schemas["InvoiceLineDetail"];
export type EditInvoiceRequest = Schemas["EditInvoiceRequest"];
export type EditInvoiceLineRequest = Schemas["EditInvoiceLineRequest"];
export type InvoiceEditedResponse = Schemas["InvoiceEditedResponse"];
export type InvoiceDeleted = Schemas["InvoiceDeleted"];
export type DeletedInvoiceSummary = Schemas["DeletedInvoiceSummary"];
export type DeletedInvoiceDetail = Schemas["DeletedInvoiceDetail"];

export type CreateQuotationRequest = Schemas["CreateQuotationRequest"];
export type ConvertQuotationRequest = Schemas["ConvertQuotationRequest"];
export type QuotationCreatedResponse = Schemas["QuotationCreatedResponse"];
export type QuotationSummary = Schemas["QuotationSummary"];
export type QuotationDetail = Schemas["QuotationDetail"];
export type EditQuotationRequest = Schemas["EditQuotationRequest"];
export type QuotationEditedResponse = Schemas["QuotationEditedResponse"];
export type QuotationDeleted = Schemas["QuotationDeleted"];

export type CreateCreditNoteRequest = Schemas["CreateCreditNoteRequest"];
export type CreditNoteCreatedResponse = Schemas["CreditNoteCreatedResponse"];
export type CreditNoteSummary = Schemas["CreditNoteSummary"];
export type CreditNoteDetail = Schemas["CreditNoteDetail"];

// --- Purchase orders (Phase 6) ---------------------------------------------------------------

export type CreatePurchaseOrderRequest = Schemas["CreatePurchaseOrderRequest"];
export type PurchaseOrderCreatedResponse = Schemas["PurchaseOrderCreatedResponse"];
export type PurchaseOrderSummary = Schemas["PurchaseOrderSummary"];
export type PurchaseOrderDetail = Schemas["PurchaseOrderDetail"];

export type CreateSupplierInvoiceRequest = Schemas["CreateSupplierInvoiceRequest"];
export type SupplierInvoiceCreatedResponse = Schemas["SupplierInvoiceCreatedResponse"];
export type SupplierInvoiceSummary = Schemas["SupplierInvoiceSummary"];
export type SupplierInvoiceDetail = Schemas["SupplierInvoiceDetail"];
export type SupplierInvoicePaymentLine = Schemas["SupplierInvoicePaymentLine"];
export type RecordSupplierPaymentRequest = Schemas["RecordSupplierPaymentRequest"];
export type SupplierPaymentRecordedResponse = Schemas["SupplierPaymentRecordedResponse"];

// --- History ---------------------------------------------------------------------------------

export type AuditEntry = Schemas["AuditEntry"];
export type RecordHistory = Schemas["RecordHistoryResponse"];
export type AuditLogResponse = Schemas["AuditLogResponse"];
export type AuditFacets = Schemas["AuditFacetsResponse"];
export type AuditActor = Schemas["AuditActorDto"];
export type DocumentVersionSummary = Schemas["DocumentVersionSummary"];
export type DocumentVersionDetail = Schemas["DocumentVersionDetail"];

// --- Errors ----------------------------------------------------------------------------------

/**
 * RFC 9457. Our API adds `correlationId` — the reference a user reads back to you off the error
 * screen, and the only link between "it broke" and the log line that says why.
 */
export type ProblemDetails = Schemas["ProblemDetails"] & {
  correlationId?: string;
  code?: string;
  errors?: Record<string, string[]>;
};

/** Every path the API exposes, for anything that wants to be exhaustive about them. */
export type ApiPaths = paths;
