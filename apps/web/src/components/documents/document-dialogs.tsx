"use client";

/**
 * The dialogs a quotation and a proforma invoice share: converting to an invoice, and voiding.
 *
 * One copy, because both documents become an invoice by exactly the same rules — cash or credit, the
 * invoice's own date, an optional PO, and for service work a cost — and the server enforces those rules the
 * same way for both. Two copies of the form would drift from each other long before the server did.
 */

import { useState } from "react";
import { ApiError } from "@/lib/api";
import { today } from "@/lib/period";
import { Button, Dialog, ErrorBanner, Input, Select, toast } from "@/components/ui";

export interface ConvertToInvoiceRequest {
  type: string;
  date: string;
  purchaseOrderNo: string | null;
  contactPerson: string | null;
  documentCost: number | null;
}

/**
 * The terms that turn a quotation or proforma into an invoice — plus, for service work, its cost.
 *
 * Item lines carry their cost from the item master. Service work has no such source, so the figure is asked
 * for here, at the point the work is committed to and the cost is real. The server enforces the same rule
 * and refuses the conversion without it (400) — this field is the convenience, not the guarantee.
 */
export function ConvertToInvoiceDialog({
  open,
  onOpenChange,
  documentLabel,
  isService,
  storedCost = 0,
  defaultContact,
  onConverted,
  convert,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** What is being converted, as a heading reads it — "Quotation STQ-941", "Proforma invoice STPI-3". */
  documentLabel: string;
  /** Item vs service, decided by the server on whether any line resolves to an item. */
  isService: boolean;
  /** Any cost already recorded, to seed the field so nothing captured earlier is retyped. */
  storedCost?: number;
  defaultContact: string | null | undefined;
  onConverted: (invoiceId: number) => void;
  convert: (request: ConvertToInvoiceRequest) => Promise<{ id: number; number: string; total: number }>;
}) {
  const [type, setType] = useState("Credit");
  const [date, setDate] = useState(today);
  const [po, setPo] = useState("");
  const [cost, setCost] = useState(storedCost > 0 ? String(storedCost) : "");
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  // Zero is a valid answer; blank is not. Work that genuinely cost nothing is a claim someone can make, but
  // it has to be made — defaulting a blank box to zero is how an invoice comes to report 100% margin.
  const costEntered = cost.trim() !== "" && Number.isFinite(Number(cost)) && Number(cost) >= 0;
  const canConvert = !isService || costEntered;

  async function submit() {
    setSubmitting(true);
    setError(null);
    try {
      const invoice = await convert({
        type,
        date,
        purchaseOrderNo: po || null,
        contactPerson: defaultContact || null,
        // Only service work sends one; the server derives an item document's from its lines.
        documentCost: isService ? Number(cost) : null,
      });
      toast.success(`Invoice ${invoice.number} raised from ${documentLabel}.`);
      onOpenChange(false);
      onConverted(invoice.id);
    } catch (e) {
      // A second conversion, the credit limit, and a live proforma all arrive as a 409.
      setError(e as ApiError);
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <Dialog
      open={open}
      onOpenChange={onOpenChange}
      title={`Convert ${documentLabel} to an invoice`}
      description="The invoice is taxed at its own date and gets a real number, a ledger charge and — for item lines — a stock issue. This cannot be undone."
      footer={
        <>
          <Button variant="secondary" onClick={() => onOpenChange(false)} disabled={submitting}>
            Cancel
          </Button>
          <Button onClick={submit} pending={submitting} disabled={!canConvert}>
            Convert
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        {error && <ErrorBanner message={error.message} correlationId={error.correlationId} />}

        <Select label="Type" value={type} onChange={(e) => setType(e.target.value)}>
          <option value="Credit">Credit</option>
          <option value="Cash">Cash</option>
        </Select>

        <Input label="Invoice date" type="date" value={date} onChange={(e) => setDate(e.target.value)} />
        <Input label="PO number" value={po} onChange={(e) => setPo(e.target.value)} />

        {isService && (
          <Input
            label="Cost"
            inputMode="decimal"
            value={cost}
            onChange={(e) => setCost(e.target.value)}
            placeholder="0"
            hint="What this work costs you. Required — it is the basis for the invoice's profit, and an invoice with no cost reports as pure profit."
          />
        )}
      </div>
    </Dialog>
  );
}

/** Voids a document — soft, recoverable and audited, with a reason the server requires. */
export function VoidDocumentDialog({
  open,
  onOpenChange,
  documentLabel,
  description,
  onVoided,
  voidDocument,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** "quotation STQ-941", "proforma invoice STPI-3" — as the button and title read it. */
  documentLabel: string;
  description: string;
  onVoided: () => void;
  voidDocument: (reason: string) => Promise<unknown>;
}) {
  const [reason, setReason] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  async function submit() {
    setSubmitting(true);
    setError(null);
    try {
      await voidDocument(reason);
      onOpenChange(false);
      onVoided();
    } catch (e) {
      setError(e as ApiError);
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <Dialog
      open={open}
      onOpenChange={onOpenChange}
      title={`Void ${documentLabel}`}
      description={description}
      footer={
        <>
          <Button variant="secondary" onClick={() => onOpenChange(false)} disabled={submitting}>
            Cancel
          </Button>
          <Button onClick={submit} pending={submitting} disabled={reason.trim().length < 10}>
            Void
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        {error && <ErrorBanner message={error.message} correlationId={error.correlationId} />}
        <Input
          label="Reason"
          value={reason}
          onChange={(e) => setReason(e.target.value)}
          hint="At least 10 characters — this is recorded on the audit trail."
          placeholder="Why is this being voided?"
        />
      </div>
    </Dialog>
  );
}
