/**
 * The saved shapes of the quotation and invoice create screens' drafts — and how a draft is turned into
 * the document it would print as.
 *
 * The shapes live here rather than in the two screens because the Drafts tab reads them too, to print a
 * draft without opening it. One declaration keeps the reader and the writer from drifting: a screen that
 * changes its state changes it here, and bumps the version beside it.
 *
 * The server never parses a draft (see `DocumentDraft`), so it is the browser that reads one and sends
 * what the document would be raised with — the same mapping the screen's own save makes.
 */

import { getActiveCompany } from "@/lib/api";
import { readPayload } from "@/lib/drafts";
import { MINOR_UNITS_PER_MAJOR, QUANTITY_SCALE } from "@/lib/money";
import { today } from "@/lib/period";
import type { DocumentKind, DraftLine } from "./line-draft";

/** The quotation draft's saved shape. Bump it when the state below changes meaning — see `readPayload`. */
export const QUOTATION_DRAFT_VERSION = 1;

export interface QuotationDraftState {
  kind: DocumentKind;
  companyId: string;
  customerId: string;
  date: string;
  validity: string;
  contact: string;
  documentDiscount: string;
  lines: DraftLine[];
}

/** The invoice draft's saved shape. Bump it when the state below changes meaning — see `readPayload`. */
export const INVOICE_DRAFT_VERSION = 1;

export interface InvoiceDraftState {
  kind: DocumentKind;
  companyId: string;
  customerId: string;
  type: string;
  date: string;
  po: string;
  contact: string;
  documentDiscount: string;
  serviceCost: string;
  lines: DraftLine[];
}

/** The document discount as typed, clamped to 0–100 — what the screen itself applies. */
export function documentDiscountPercent(typed: string): number {
  const value = Number(typed);
  return Number.isFinite(value) ? Math.min(100, Math.max(0, value)) : 0;
}

/** The lines back in the major-unit decimals the API expects, at the boundary and nowhere else. */
export const linesForApi = (lines: readonly DraftLine[]) =>
  lines.map((l) => ({
    itemId: l.itemId,
    itemCode: l.itemCode,
    description: l.description,
    quantity: l.quantity / QUANTITY_SCALE,
    unitPrice: l.unitPrice / MINOR_UNITS_PER_MAJOR,
    discountPercent: l.discountPercent,
    cost: l.cost === null ? null : l.cost / MINOR_UNITS_PER_MAJOR,
  }));

/** Where a draft prints from, and what to send. */
export interface DraftPrintRequest {
  path: string;
  body: unknown;
}

/**
 * The print request for a quotation or invoice draft, or null when the payload is from a shape this
 * version no longer understands. Gaps a draft is allowed to have are filled the way the screen would:
 * no company yet is the one being worked in, no date yet is today, no customer prints a blank Bill To.
 */
export function draftPrintRequest(docType: string, payload: string): DraftPrintRequest | null {
  if (docType === "QUOTATION") {
    const state = readPayload<QuotationDraftState>(payload, QUOTATION_DRAFT_VERSION);
    if (state === null) return null;

    return {
      path: "/api/quotations/draft-pdf",
      body: {
        companyId: companyOf(state.companyId),
        customerId: state.customerId === "" ? null : Number(state.customerId),
        date: state.date || today(),
        contactPerson: state.contact || null,
        validity: state.validity || null,
        documentDiscountPercent: documentDiscountPercent(state.documentDiscount),
        lines: linesForApi(state.lines),
      },
    };
  }

  if (docType === "INVOICE") {
    const state = readPayload<InvoiceDraftState>(payload, INVOICE_DRAFT_VERSION);
    if (state === null) return null;

    return {
      path: "/api/invoices/draft-pdf",
      body: {
        companyId: companyOf(state.companyId),
        customerId: state.customerId === "" ? null : Number(state.customerId),
        type: state.type,
        date: state.date || today(),
        purchaseOrderNo: state.po || null,
        contactPerson: state.contact || null,
        documentDiscountPercent: documentDiscountPercent(state.documentDiscount),
        lines: linesForApi(state.lines),
      },
    };
  }

  return null;
}

const companyOf = (chosen: string) => (chosen === "" ? getActiveCompany() : Number(chosen));
