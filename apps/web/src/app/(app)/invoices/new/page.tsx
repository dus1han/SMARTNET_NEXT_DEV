"use client";

/**
 * Raise an invoice — the create screen, Phase 5.
 *
 * The header fields and the foot are the invoice's own; the line-item draft in the middle is the shared
 * editor (`components/documents/line-draft`), which quotations use too. The draft lives in the browser
 * and is posted whole, once — the legacy server-session cart is gone (D4). One VAT rate, the company's,
 * resolved once by the server (never per keystroke) so the foot reads like a real invoice and matches the
 * figure the save returns.
 */

import { useMemo, useState } from "react";
import { useRouter } from "next/navigation";
import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { ArrowLeft } from "lucide-react";
import { ApiError } from "@/lib/api";
import { createInvoice, getCreditStatus, getInvoiceTaxRate } from "@/lib/invoices";
import { listCompanies, listCustomers } from "@/lib/customers";
import { listItems } from "@/lib/items";
import { today } from "@/lib/period";
import { formatAmount, MINOR_UNITS_PER_MAJOR, QUANTITY_SCALE } from "@/lib/money";
import { cn } from "@/lib/cn";
import { PageHeader } from "@/components/shell/app-shell";
import { formatMoney, formatReportDate } from "@/components/reports";
import { AlertTriangle } from "lucide-react";
import { Button, Card, Dialog, ErrorBanner, FadeIn, Input, Select, toast } from "@/components/ui";
import {
  CustomerCombobox,
  customerContactNames,
  LineDraftEditor,
  linesArePostable,
  useDraftTotals,
  type DocumentKind,
  type DraftLine,
} from "@/components/documents/line-draft";

export default function NewInvoicePage() {
  const router = useRouter();
  const companies = useQuery({ queryKey: ["companies"], queryFn: listCompanies });
  const customers = useQuery({ queryKey: ["customers"], queryFn: listCustomers });
  const items = useQuery({ queryKey: ["items"], queryFn: listItems });

  const [kind, setKind] = useState<DocumentKind>("service");
  const [companyId, setCompanyId] = useState("");
  const [customerId, setCustomerId] = useState("");
  const [type, setType] = useState("Credit");
  const [date, setDate] = useState(today);
  const [po, setPo] = useState("");
  const [contact, setContact] = useState("");
  // A discount on the whole document, after the per-line discounts. Held raw so a half-typed "1." is fine.
  const [documentDiscount, setDocumentDiscount] = useState("");
  // A service invoice's cost is entered at the document level (item lines derive it from the item master).
  const [serviceCost, setServiceCost] = useState("");
  const [lines, setLines] = useState<DraftLine[]>([]);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);
  const [confirmOverLimit, setConfirmOverLimit] = useState(false);

  // The one rate the whole document carries, resolved by the server for this company on this date. Fetched
  // only when either changes — never while typing lines, which is the shared editor's whole promise.
  const taxRate = useQuery({
    queryKey: ["invoice-tax-rate", companyId, date],
    queryFn: () => getInvoiceTaxRate(Number(companyId), date),
    enabled: companyId !== "",
  });
  const rateError = taxRate.error as ApiError | null;
  const ratePercent = taxRate.data?.percentage ?? 0;

  const docPercent = useMemo(() => {
    const value = Number(documentDiscount);
    return Number.isFinite(value) ? Math.min(100, Math.max(0, value)) : 0;
  }, [documentDiscount]);

  // The contact person comes from the customer: the master stores each contact in one `;`-separated field.
  const selectedCustomer = customers.data?.find((c) => String(c.id) === customerId) ?? null;
  const contactOptions = customerContactNames(selectedCustomer);

  // The customer's credit standing, fetched when both company and customer are chosen — the same figures
  // the server-side gate uses. Never per keystroke; only when the customer or company changes.
  const creditStatus = useQuery({
    queryKey: ["credit-status", customerId, companyId],
    queryFn: () => getCreditStatus(Number(customerId), Number(companyId)),
    enabled: companyId !== "" && customerId !== "",
  });

  const totals = useDraftTotals(lines, ratePercent, docPercent);

  // Would this invoice take the customer past their limit? A 0 limit means "no limit". Compared in
  // major units — the totals are in minor (fils), the API's figures in major.
  const credit = creditStatus.data;
  const totalMajor = totals.total / MINOR_UNITS_PER_MAJOR;
  const projected = (credit?.outstanding ?? 0) + totalMajor;
  const overLimit = credit != null && credit.creditLimit > 0 && projected > credit.creditLimit;

  const canSubmit = companyId !== "" && customerId !== "" && linesArePostable(lines);

  // The button gates on the limit: a breach asks for confirmation first; otherwise it saves straight.
  function attemptSubmit() {
    if (overLimit) {
      setConfirmOverLimit(true);
      return;
    }
    void submit(false);
  }

  async function submit(acknowledgeCreditLimit: boolean) {
    setConfirmOverLimit(false);
    setSubmitting(true);
    setError(null);
    try {
      const created = await createInvoice({
        companyId: Number(companyId),
        customerId: Number(customerId),
        type,
        date,
        purchaseOrderNo: po || null,
        contactPerson: contact || null,
        documentDiscountPercent: docPercent,
        acknowledgeCreditLimit,
        // Service invoices carry a document-level cost; item invoices derive it from the line item costs.
        documentCost: kind === "service" && serviceCost !== "" ? Number(serviceCost) : null,
        // Back to the major-unit decimals the API expects, at the boundary and nowhere else.
        lines: lines.map((l) => ({
          itemId: l.itemId,
          itemCode: l.itemCode,
          description: l.description,
          quantity: l.quantity / QUANTITY_SCALE,
          unitPrice: l.unitPrice / MINOR_UNITS_PER_MAJOR,
          discountPercent: l.discountPercent,
          cost: l.cost === null ? null : l.cost / MINOR_UNITS_PER_MAJOR,
        })),
      });
      toast.success(`Invoice ${created.number} raised — ${formatMoney(created.total)}.`);
      router.push(`/invoices/${created.id}`);
    } catch (e) {
      const err = e as ApiError;
      // A credit-limit breach is a soft gate, not a failure: catch it, and ask the user to confirm
      // rather than showing an error. Once they confirm, the retry carries the acknowledgement and saves.
      if (err.status === 409 && err.code === "credit_limit_exceeded" && !acknowledgeCreditLimit) {
        setConfirmOverLimit(true);
      } else {
        setError(err);
      }
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <FadeIn className="space-y-6">
      <Link
        href="/invoices"
        className="inline-flex items-center gap-1.5 text-sm text-muted transition-colors hover:text-text"
      >
        <ArrowLeft className="size-4" aria-hidden />
        All invoices
      </Link>

      <PageHeader title="New invoice" description="The whole document is posted once — nothing is saved while you type." />

      {error && <ErrorBanner message={error.message} correlationId={error.correlationId} />}

      <Card className="grid gap-4 p-5 sm:grid-cols-2 lg:grid-cols-3">
        <Select label="Company" value={companyId} onChange={(e) => setCompanyId(e.target.value)}>
          <option value="">Select…</option>
          {companies.data?.map((c) => (
            <option key={c.id} value={c.id}>{c.name}</option>
          ))}
        </Select>

        <CustomerCombobox
          customers={customers.data ?? []}
          value={customerId}
          onChange={(id) => {
            setCustomerId(id);
            // Default the contact to the customer's first — the common case is a customer with one.
            const picked = customers.data?.find((c) => String(c.id) === id);
            setContact(customerContactNames(picked)[0] ?? "");
          }}
        />

        <Select label="Type" value={type} onChange={(e) => setType(e.target.value)}>
          <option value="Credit">Credit</option>
          <option value="Cash">Cash</option>
        </Select>

        <Input label="Date" type="date" value={date} onChange={(e) => setDate(e.target.value)} />
        <Input label="PO number" value={po} onChange={(e) => setPo(e.target.value)} />

        {contactOptions.length > 0 ? (
          <Select label="Contact person" value={contact} onChange={(e) => setContact(e.target.value)}>
            {!contactOptions.includes(contact) && <option value="">Select…</option>}
            {contactOptions.map((person) => (
              <option key={person} value={person}>{person}</option>
            ))}
          </Select>
        ) : (
          <Input
            label="Contact person"
            value={contact}
            onChange={(e) => setContact(e.target.value)}
            hint={customerId === "" ? "Select a customer to pick from its contacts." : undefined}
          />
        )}
      </Card>

      {credit != null && credit.creditLimit > 0 && (
        <CreditWarning
          limit={credit.creditLimit}
          outstanding={credit.outstanding}
          projected={projected}
          overLimit={overLimit}
        />
      )}

      {rateError && <ErrorBanner message={rateError.message} correlationId={rateError.correlationId} />}

      <LineDraftEditor
        kind={kind}
        onKindChange={setKind}
        lines={lines}
        onLinesChange={setLines}
        items={items.data ?? []}
      />

      <div className="grid gap-4 sm:grid-cols-2">
        <div />
        <Card className="space-y-2 p-5">
          <Row label="Date" value={formatReportDate(date)} />
          <Row label="Subtotal" value={formatAmount(totals.subtotal)} />

          {totals.discount - totals.docDiscount > 0 && (
            <Row label="Line discounts" value={`− ${formatAmount(totals.discount - totals.docDiscount)}`} />
          )}

          {/* A discount on the whole document — after the line discounts, before VAT. */}
          <div className="flex items-center justify-between gap-3 py-0.5">
            <label htmlFor="doc-discount" className="text-muted">Document discount %</label>
            <input
              id="doc-discount"
              inputMode="decimal"
              value={documentDiscount}
              onChange={(e) => setDocumentDiscount(e.target.value)}
              placeholder="0"
              className={cn(
                "w-20 rounded border border-subtle bg-surface px-2 py-1 text-right tabular text-text",
                "focus:border-primary focus:outline-none focus:ring-2 focus:ring-ring/25",
              )}
            />
          </div>

          {totals.docDiscount > 0 && (
            <Row label="Document discount" value={`− ${formatAmount(totals.docDiscount)}`} />
          )}

          <Row label="Net" value={formatAmount(totals.net)} />
          <Row
            label={companyId === "" ? "VAT" : taxRate.isPending ? "VAT (…)" : `VAT (${ratePercent}%)`}
            value={formatAmount(totals.vat)}
          />
          <div className="border-t border-subtle pt-2">
            <Row label="Total" value={formatAmount(totals.total)} strong />
          </div>

          {/* A service invoice's cost is entered here (item invoices derive it from the item master). It is
              the margin basis — it is not added to the total. */}
          {kind === "service" && (
            <div className="flex items-center justify-between gap-3 border-t border-subtle pt-2">
              <label htmlFor="service-cost" className="text-muted">Cost</label>
              <input
                id="service-cost"
                inputMode="decimal"
                value={serviceCost}
                onChange={(e) => setServiceCost(e.target.value)}
                placeholder="0"
                className={cn(
                  "w-28 rounded border border-subtle bg-surface px-2 py-1 text-right tabular text-text",
                  "focus:border-primary focus:outline-none focus:ring-2 focus:ring-ring/25",
                )}
              />
            </div>
          )}

          <Button className="mt-2 w-full" onClick={attemptSubmit} pending={submitting} disabled={!canSubmit}>
            Raise invoice
          </Button>
        </Card>
      </div>

      <Dialog
        open={confirmOverLimit}
        onOpenChange={setConfirmOverLimit}
        title="Over the credit limit"
        description={
          credit
            ? `This invoice would take ${selectedCustomer?.name ?? "the customer"} to ${formatMoney(projected)}, ` +
              `past their credit limit of ${formatMoney(credit.creditLimit)} (${formatMoney(credit.outstanding)} already outstanding). ` +
              `Confirm to raise it anyway.`
            : undefined
        }
        footer={
          <>
            <Button variant="secondary" onClick={() => setConfirmOverLimit(false)} disabled={submitting}>
              Cancel
            </Button>
            <Button onClick={() => submit(true)} pending={submitting}>
              Raise anyway
            </Button>
          </>
        }
      />
    </FadeIn>
  );
}

/**
 * The credit advisory shown when a customer is picked: their outstanding, their limit, and the headroom
 * left — turning red once this draft would breach it. Enforcement being on means the server will reject a
 * breach outright; off means it is advisory and the save asks for confirmation. This is the heads-up the
 * legacy client-side check gave, kept as a courtesy on top of the server-side gate that actually holds.
 */
function CreditWarning({ limit, outstanding, projected, overLimit }: {
  limit: number;
  outstanding: number;
  projected: number;
  overLimit: boolean;
}) {
  const available = limit - outstanding;

  return (
    <Card
      className={cn(
        "flex flex-wrap items-center gap-x-6 gap-y-2 p-4 text-sm",
        overLimit ? "border-danger/40 bg-danger-subtle" : "border-subtle",
      )}
    >
      {overLimit && <AlertTriangle className="size-5 shrink-0 text-danger" aria-hidden />}
      <Figure label="Credit limit" value={formatMoney(limit)} over={overLimit} />
      <Figure label="Outstanding" value={formatMoney(outstanding)} over={overLimit} />
      <Figure
        label={overLimit ? "Would become" : "Available"}
        value={formatMoney(overLimit ? projected : available)}
        over={overLimit}
        strong={overLimit}
      />
      {overLimit && (
        <span className="font-medium text-danger">
          This invoice puts them over their limit — you will be asked to confirm before it is raised.
        </span>
      )}
    </Card>
  );
}

function Figure({ label, value, over = false, strong = false }: { label: string; value: string; over?: boolean; strong?: boolean }) {
  return (
    <span className="flex flex-col">
      <span className="text-xs uppercase tracking-wide text-muted">{label}</span>
      <span
        className={cn(
          "tabular",
          strong ? "font-bold" : "font-medium",
          // On the red (danger-subtle) background, the solid danger colour is the legible one — never
          // danger-text, which is near-white (it is for text on a solid danger fill).
          over ? "text-danger" : "text-text",
        )}
      >
        {value}
      </span>
    </span>
  );
}

function Row({ label, value, strong = false }: { label: string; value: string; strong?: boolean }) {
  return (
    <div className="flex items-center justify-between">
      <span className={strong ? "font-semibold text-text" : "text-muted"}>{label}</span>
      <span className={`tabular ${strong ? "text-lg font-bold text-text" : "text-text"}`}>{value}</span>
    </div>
  );
}
