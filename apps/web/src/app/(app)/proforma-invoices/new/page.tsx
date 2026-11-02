"use client";

/**
 * Raise a proforma invoice on its own — no quotation behind it.
 *
 * The same shared line-item editor the quotation and invoice screens use, given a document that charges
 * nothing: a proforma is what the customer pays against, and the invoice is raised when it is converted.
 * One company VAT rate, resolved by the server (the quotation preview, since a proforma is gated like one).
 *
 * Unlike the quotation screen it keeps no draft: a proforma is usually raised in one sitting from figures
 * already agreed, and it is cheap to void and raise again.
 */

import { useMemo, useState } from "react";
import { useRouter } from "next/navigation";
import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { ArrowLeft } from "lucide-react";
import { ApiError } from "@/lib/api";
import { getQuotationTaxRate } from "@/lib/quotations";
import { createStandaloneProforma } from "@/lib/proformas";
import { listCompanies, listCustomers } from "@/lib/customers";
import { listItems } from "@/lib/items";
import { today } from "@/lib/period";
import { formatAmount } from "@/lib/money";
import { cn } from "@/lib/cn";
import { PageHeader } from "@/components/shell/app-shell";
import { formatMoney, formatReportDate } from "@/components/reports";
import { Button, Card, ErrorBanner, FadeIn, Input, Select, toast } from "@/components/ui";
import {
  CustomerCombobox,
  customerContactNames,
  LineDraftEditor,
  linesArePostable,
  useDraftTotals,
  type DocumentKind,
  type DraftLine,
} from "@/components/documents/line-draft";
import { documentDiscountPercent, linesForApi } from "@/components/documents/draft-print";

export default function NewProformaPage() {
  const router = useRouter();
  const companies = useQuery({ queryKey: ["companies"], queryFn: listCompanies });
  const customers = useQuery({ queryKey: ["customers"], queryFn: listCustomers });
  const items = useQuery({ queryKey: ["items"], queryFn: listItems });

  const [kind, setKind] = useState<DocumentKind>("service");
  const [companyId, setCompanyId] = useState("");
  const [customerId, setCustomerId] = useState("");
  const [date, setDate] = useState(today);
  const [validity, setValidity] = useState("30 Days");
  const [contact, setContact] = useState("");
  const [documentDiscount, setDocumentDiscount] = useState("");
  const [lines, setLines] = useState<DraftLine[]>([]);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  const taxRate = useQuery({
    queryKey: ["quotation-tax-rate", companyId, date],
    queryFn: () => getQuotationTaxRate(Number(companyId), date),
    enabled: companyId !== "",
  });
  const rateError = taxRate.error as ApiError | null;
  const ratePercent = taxRate.data?.percentage ?? 0;

  const docPercent = useMemo(() => documentDiscountPercent(documentDiscount), [documentDiscount]);

  const selectedCustomer = customers.data?.find((c) => String(c.id) === customerId) ?? null;
  const contactOptions = customerContactNames(selectedCustomer);

  const totals = useDraftTotals(lines, ratePercent, docPercent);
  const canSubmit = companyId !== "" && customerId !== "" && linesArePostable(lines);

  async function submit() {
    setSubmitting(true);
    setError(null);
    try {
      const created = await createStandaloneProforma({
        companyId: Number(companyId),
        customerId: Number(customerId),
        date,
        contactPerson: contact || null,
        validity: validity || null,
        documentDiscountPercent: docPercent,
        lines: linesForApi(lines),
      });
      toast.success(`Proforma invoice ${created.number} raised — ${formatMoney(created.total)}.`);
      router.push(`/proforma-invoices/${created.id}`);
    } catch (e) {
      setError(e as ApiError);
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <FadeIn className="space-y-6">
      <Link
        href="/proforma-invoices"
        className="inline-flex items-center gap-1.5 text-sm text-muted transition-colors hover:text-text"
      >
        <ArrowLeft className="size-4" aria-hidden />
        All proforma invoices
      </Link>

      <PageHeader
        title="New proforma invoice"
        description="What the customer pays against. It charges nothing — the invoice is raised when it is converted."
      />

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
            const picked = customers.data?.find((c) => String(c.id) === id);
            setContact(customerContactNames(picked)[0] ?? "");
          }}
        />

        <Input label="Date" type="date" value={date} onChange={(e) => setDate(e.target.value)} />
        <Input
          label="Valid for"
          value={validity}
          onChange={(e) => setValidity(e.target.value)}
          hint="How long the price holds — e.g. 30 Days."
        />

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
            <Row label="Amount due" value={formatAmount(totals.total)} strong />
          </div>

          <Button className="mt-2 w-full" onClick={submit} pending={submitting} disabled={!canSubmit}>
            Raise proforma invoice
          </Button>
        </Card>
      </div>
    </FadeIn>
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
