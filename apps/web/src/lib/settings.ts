import type {
  BusinessRule,
  CompanyProfile,
  CompanySummary,
  DocumentSeries,
  MailSettings,
  NumberPreview,
  SeriesInitialisation,
  TaxRate,
} from "@smartnet/api-client";
import { api, API_BASE_URL } from "./api";

// The types are generated from the API's OpenAPI schema — see packages/api-client. They are
// re-exported here so a screen imports one thing, but they are NOT declared here: a hand-written
// `interface MailSettings` would go on compiling long after the API stopped agreeing with it.
export type {
  BusinessRule,
  CompanyProfile,
  CompanySummary,
  DocumentSeries,
  MailSettings,
  NumberPreview,
  SeriesInitialisation,
  TaxRate,
};

// --- Companies & settings ----------------------------------------------------------------------

export const listCompanies = () => api<CompanySummary[]>("/api/companies");

// Every call below takes the company being configured. Settings are per-entity — Smart Net and
// Smart Technologies each have their own header, tax rates, numbering and mail — and the Settings
// screen names which one on the request rather than relying on an ambient "working in" choice that
// no longer exists.

export const getCompany = (companyId: number) =>
  api<CompanyProfile>("/api/settings/company", { companyId });

export const saveCompany = (profile: CompanyProfile, reason: string, companyId: number) =>
  api<CompanyProfile>("/api/settings/company", { method: "PUT", body: profile, reason, companyId });

// The logo is a binary upload / image response, so it uses fetch directly rather than the JSON `api` helper —
// but the same auth (the httpOnly cookie via credentials) and the same per-company header.

/** The company's logo as an object URL, or null when it has none. The caller revokes the URL when done. */
export async function getCompanyLogoUrl(companyId: number): Promise<string | null> {
  const response = await fetch(`${API_BASE_URL}/api/settings/company/logo`, {
    headers: { "X-Company-Id": String(companyId) },
    credentials: "include",
  });
  if (response.status === 204 || !response.ok) {
    return null;
  }
  const blob = await response.blob();
  return blob.size === 0 ? null : URL.createObjectURL(blob);
}

/** Uploads (replaces) the company's logo — a PNG/JPEG/GIF/WebP/SVG up to 2 MB. */
export async function uploadCompanyLogo(companyId: number, file: File): Promise<void> {
  const form = new FormData();
  form.append("file", file);
  const response = await fetch(`${API_BASE_URL}/api/settings/company/logo`, {
    method: "POST",
    headers: { "X-Company-Id": String(companyId) },
    credentials: "include",
    body: form,
  });
  if (!response.ok) {
    throw new Error((await response.text().catch(() => "")) || "The logo upload failed.");
  }
}

export const deleteCompanyLogo = (companyId: number) =>
  api<void>("/api/settings/company/logo", { method: "DELETE", companyId });

/**
 * A new trading entity, and the setup it needs to be usable.
 *
 * No VAT rate is asked for: a VAT-registered company inherits the rate the others charge today, and an
 * unregistered one gets a zero rate only. The tick is the whole of the VAT decision.
 */
export interface CreateCompany {
  name: string;
  isVatRegistered: boolean;
  businessRegistrationNo: string | null;
  numberPrefix: string;
}

export interface CompanyCreated {
  id: number;
  name: string;
  taxRatesCreated: number;
  numberSeriesCreated: number;
  emailTemplatesCreated: number;
}

/** Dev_Admin only. Creates the company, its tax rates, nine numbering series and five templates. */
export const createCompany = (company: CreateCompany, reason: string) =>
  api<CompanyCreated>("/api/companies", { method: "POST", body: company, reason });

export const getBusinessRules = (companyId: number) =>
  api<BusinessRule[]>("/api/settings/business-rules", { companyId });

export const saveBusinessRules = (rules: BusinessRule[], reason: string, companyId: number) =>
  api<void>("/api/settings/business-rules", { method: "PUT", body: rules, reason, companyId });

export const getTaxRates = (companyId: number) =>
  api<TaxRate[]>("/api/settings/tax-rates", { companyId });

/**
 * The business VAT rate, set once for every VAT-registered company.
 *
 * VAT is a national rate: it is not set per company, so this carries no company. The server fans it out
 * across all VAT-registered entities as their default from `effectiveFrom`, leaving the previous rate in
 * place so it governs everything before that date. There is no delete — a rate that has taxed a document
 * is history; to change it, set a new one from a later date.
 */
export interface SetVatRate {
  name: string;
  percentage: number;
  effectiveFrom: string;
}

/** Dev_Admin only. Returns how many companies the change touched. */
export const setVatRate = (rate: SetVatRate, reason: string) =>
  api<{ companiesAffected: number }>("/api/settings/vat-rate", { method: "POST", body: rate, reason });

/**
 * Shift when one company adopts its rate — the only per-company tax edit.
 *
 * The rate and percentage are business-wide; a single company may only vary the date it starts, for the
 * case where one entity changed its systems on a different day.
 */
export const updateTaxRateFrom = (id: number, effectiveFrom: string, reason: string, companyId: number) =>
  api<void>(`/api/settings/tax-rates/${id}`, {
    method: "PUT",
    body: { effectiveFrom },
    reason,
    companyId,
  });

// --- Mail ----------------------------------------------------------------------------------------

export const getMailSettings = (companyId: number) =>
  api<MailSettings>("/api/settings/mail", { companyId });

/** `password: null` leaves the stored one alone — which is what to send when it wasn't retyped. */
export const saveMailSettings = (
  settings: MailSettings & { password: string | null },
  reason: string,
  companyId: number,
) => api<void>("/api/settings/mail", { method: "PUT", body: settings, reason, companyId });

export const sendTestEmail = (to: string, companyId: number) =>
  api<void>("/api/settings/mail/test", { method: "POST", body: { to }, companyId });

// --- Document numbering --------------------------------------------------------------------------

export const getNumbering = (companyId: number) =>
  api<DocumentSeries[]>("/api/settings/numbering", { companyId });

export const saveSeries = (
  id: number,
  prefix: string,
  padding: number,
  reason: string,
  companyId: number,
) =>
  api<void>(`/api/settings/numbering/${id}`, {
    method: "PUT",
    body: { prefix, padding },
    reason,
    companyId,
  });

export const previewNumber = (prefix: string, nextNumber: number, padding: number) =>
  api<NumberPreview>("/api/settings/numbering/preview", {
    method: "POST",
    body: { prefix, nextNumber, padding },
  });

/**
 * Reads the legacy numbering and points each series at the next unused number.
 *
 * Run `apply: false` first and read what it says. Run it for real at cutover, immediately after the
 * legacy app stops issuing that document type — any invoice the old app raises afterwards takes a
 * number this app also believes is free.
 */
export const initialiseNumbering = (apply: boolean, reason: string, companyId: number) =>
  api<SeriesInitialisation[]>(`/api/settings/numbering/initialise?apply=${apply}`, {
    method: "POST",
    reason,
    companyId,
  });

// --- Labels ---------------------------------------------------------------------------------------

/** Human labels for the seven business rules. The keys themselves are the server's contract. */
export const RULE_LABELS: Record<string, string> = {
  "credit_limit.enforced": "Enforce customer credit limits",
  "payment_terms.default_days": "Default payment terms (days)",
  "stock.reorder_level": "Low-stock reorder level",
  "quotation.validity_days": "Quotation validity (days)",
  "discount.max_percent": "Maximum discount (%)",
  "vat.rounding_mode": "VAT rounding (line or document)",
  "invoice.due_reminder_days": "Invoice due reminder (days before)",
};

/** The tokens a numbering prefix may contain. Anything else is literal text. */
export const PREFIX_TOKENS = [
  { token: "{YY}", meaning: "2-digit year — 26" },
  { token: "{YYYY}", meaning: "4-digit year — 2026" },
  { token: "{MON}", meaning: "month — JUL" },
  { token: "{MM}", meaning: "month number — 07" },
];

// --- Database backups ----------------------------------------------------------------------------

export interface BackupSummary {
  name: string;
  sizeBytes: number;
  modifiedUtc: string;
}

/** No password field: the API never returns one. `hasPassword` says only whether one is stored. */
export interface BackupSettings {
  enabled: boolean;
  host: string;
  port: number;
  username: string | null;
  hasPassword: boolean;
  useTls: boolean;
  acceptAnyCertificate: boolean;
  remotePath: string;
  safetyPath: string;
  retention: number;
  /** False when the deployment has no privileged database credential — restore is then impossible. */
  restoreAvailable: boolean;
}

/** `password: null` leaves the stored one alone — what the form sends when only the port changed. */
export type SaveBackupSettings = Omit<BackupSettings, "hasPassword" | "restoreAvailable"> & {
  password: string | null;
};

export const getBackupSettings = () => api<BackupSettings>("/api/backups/settings");

export const saveBackupSettings = (settings: SaveBackupSettings, reason: string) =>
  api<void>("/api/backups/settings", { method: "PUT", body: settings, reason });

export const listBackups = () => api<BackupSummary[]>("/api/backups");

export const takeBackupNow = (reason: string) =>
  api<{ name: string }>("/api/backups", { method: "POST", reason });

/** Restores from a backup already on the store. Destructive; `confirm` must be the word RESTORE. */
export const restoreBackup = (name: string, reason: string) =>
  api<{ safetyBackup: string }>(`/api/backups/${encodeURIComponent(name)}/restore`, {
    method: "POST",
    body: { confirm: "RESTORE" },
    reason,
  });

/** Restores from a file the administrator uploaded. Same warning applies, more so. */
export const restoreFromUpload = (file: File, reason: string) => {
  const form = new FormData();
  form.append("file", file);
  form.append("confirm", "RESTORE");

  return api<{ safetyBackup: string }>("/api/backups/restore", { method: "POST", body: form, reason });
};

/** Download URLs are plain navigations — the auth cookie rides along, same as document downloads. */
export const backupDownloadUrl = (name: string) =>
  `${API_BASE_URL}/api/backups/${encodeURIComponent(name)}/download`;

export const freshBackupDownloadUrl = () => `${API_BASE_URL}/api/backups/download`;
