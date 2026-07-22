"use client";

/**
 * Raise a job card — Phase 6 slice 3.
 *
 * A service/repair booking: the customer, the fault, the technician, and the equipment as serial-tracked
 * lines (one row per unit — the structured replacement for the legacy text blob). No money is entered here;
 * cost and sell are recorded when the job is closed.
 *
 * What is typed here is also autosaved to the server as a *draft* (`useDraftAutosave`) — a scratchpad row
 * that takes no job number and books nothing in, so an interrupted booking (which is most of them: the
 * customer is standing there) can be picked up where it was left. Raising it still goes through the one
 * create call; the draft is deleted once it has.
 */

import { useState } from "react";
import { useRouter } from "next/navigation";
import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { ArrowLeft, Plus, X } from "lucide-react";
import { ApiError } from "@/lib/api";
import { createJobCard } from "@/lib/job-cards";
import { listCompanies, listCustomers } from "@/lib/customers";
import { cn } from "@/lib/cn";
import { DRAFT_JOB_CARD } from "@/lib/drafts";
import { PageHeader } from "@/components/shell/app-shell";
import { Button, Card, ErrorBanner, FadeIn, Input, Select, toast } from "@/components/ui";
import { DraftNotices, DraftStatus } from "@/components/documents/draft-status";
import { useDraftAutosave, useDraftResume } from "@/components/documents/use-draft-autosave";
import { CustomerCombobox, customerContactNames } from "@/components/documents/line-draft";
import { today } from "@/lib/period";

interface Line { description: string; serial: string }

/** The saved shape. Bump it when the state below changes meaning — see `readPayload`. */
const DRAFT_VERSION = 1;

interface JobCardDraftState {
  companyId: string;
  customerId: string;
  date: string;
  contact: string;
  fault: string;
  remarks: string;
  technician: string;
  lines: Line[];
}

export default function NewJobCardPage() {
  const router = useRouter();
  const companies = useQuery({ queryKey: ["companies"], queryFn: listCompanies });
  const customers = useQuery({ queryKey: ["customers"], queryFn: listCustomers });

  const [companyId, setCompanyId] = useState("");
  const [customerId, setCustomerId] = useState("");
  const [date, setDate] = useState(today);
  const [contact, setContact] = useState("");
  const [fault, setFault] = useState("");
  const [remarks, setRemarks] = useState("");
  const [technician, setTechnician] = useState("");
  const [lines, setLines] = useState<Line[]>([{ description: "", serial: "" }]);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  const selectedCustomer = customers.data?.find((c) => String(c.id) === customerId) ?? null;
  const contactOptions = customerContactNames(selectedCustomer);
  const filledLines = lines.filter((l) => l.description.trim() !== "" || l.serial.trim() !== "");
  const canSubmit = companyId !== "" && customerId !== "" && filledLines.length > 0;

  // --- The draft ---------------------------------------------------------------------------------

  const resume = useDraftResume<JobCardDraftState>(DRAFT_VERSION, (state) => {
    setCompanyId(state.companyId);
    setCustomerId(state.customerId);
    setDate(state.date);
    setContact(state.contact);
    setFault(state.fault);
    setRemarks(state.remarks);
    setTechnician(state.technician);
    setLines(state.lines);
  });

  const draft = useDraftAutosave<JobCardDraftState>({
    docType: DRAFT_JOB_CARD,
    version: DRAFT_VERSION,
    state: { companyId, customerId, date, contact, fault, remarks, technician, lines },
    // The screen starts with one blank equipment row, so line *count* proves nothing here — what counts is
    // a customer, a fault described, or a row somebody actually filled in.
    worthKeeping: customerId !== "" || fault.trim() !== "" || filledLines.length > 0,
    summary: {
      partyName: selectedCustomer?.name ?? null,
      // A job card prices nothing at booking — cost and sell are recorded when the job is closed — so
      // there is no total to show, and a 0.00 would be a figure the document does not have.
      total: null,
      lineCount: filledLines.length,
    },
    resuming: resume.resuming,
    enabled: !resume.loading,
  });

  function setLine(i: number, patch: Partial<Line>) {
    setLines((current) => current.map((l, idx) => (idx === i ? { ...l, ...patch } : l)));
  }

  async function submit() {
    setSubmitting(true);
    setError(null);
    try {
      const created = await createJobCard({
        companyId: Number(companyId),
        customerId: Number(customerId),
        date,
        contactPerson: contact || null,
        faultDescription: fault || null,
        remarks: remarks || null,
        technician: technician || null,
        lines: filledLines.map((l) => ({ itemId: null, description: l.description || null, serial: l.serial || null })),
      });
      // The job card exists now, so the draft has nothing left to protect. Cleared before navigating so
      // the Drafts list is already right when the user goes back to it.
      draft.clear();
      toast.success(`Job card ${created.number} raised.`);
      // Straight to the sheet, ready to print: it is signed on collection, so the person booking the
      // job needs the paper now — not after finding the card again and hunting for a download.
      router.push(`/job-cards/${created.id}?print=1`);
    } catch (e) {
      setError(e as ApiError);
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <FadeIn className="space-y-6">
      <Link href="/job-cards" className="inline-flex items-center gap-1.5 text-sm text-muted transition-colors hover:text-text">
        <ArrowLeft className="size-4" aria-hidden />
        All job cards
      </Link>

      <PageHeader
        title={draft.draftId === null ? "New job card" : "Draft job card"}
        description="Book in a repair — cost and sell are recorded when the job is closed."
      />

      {error && <ErrorBanner message={error.message} correlationId={error.correlationId} />}

      <DraftNotices resume={resume} />
      <DraftStatus draft={draft} noun="job card" />

      <Card className="grid gap-4 p-5 sm:grid-cols-2 lg:grid-cols-3">
        <Select label="Company" value={companyId} onChange={(e) => setCompanyId(e.target.value)}>
          <option value="">Select…</option>
          {companies.data?.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
        </Select>

        <CustomerCombobox
          customers={customers.data ?? []}
          value={customerId}
          onChange={(id) => {
            setCustomerId(id);
            setContact(customerContactNames(customers.data?.find((c) => String(c.id) === id))[0] ?? "");
          }}
        />

        <Input label="Date" type="date" value={date} onChange={(e) => setDate(e.target.value)} />

        {contactOptions.length > 0 ? (
          <Select label="Contact person" value={contact} onChange={(e) => setContact(e.target.value)}>
            {!contactOptions.includes(contact) && <option value="">Select…</option>}
            {contactOptions.map((p) => <option key={p} value={p}>{p}</option>)}
          </Select>
        ) : (
          <Input label="Contact person" value={contact} onChange={(e) => setContact(e.target.value)} />
        )}

        <Input label="Technician" value={technician} onChange={(e) => setTechnician(e.target.value)} />
      </Card>

      <Card className="grid gap-4 p-5 sm:grid-cols-2">
        <Field label="Fault description" value={fault} onChange={setFault} placeholder="What is wrong with the equipment?" />
        <Field label="Remarks" value={remarks} onChange={setRemarks} placeholder="Anything else worth noting" />
      </Card>

      <Card className="space-y-3 p-5">
        <div className="flex items-center justify-between">
          <h2 className="text-sm font-semibold uppercase tracking-wider text-muted">Equipment (serial-tracked)</h2>
          <Button variant="secondary" size="sm" onClick={() => setLines((l) => [...l, { description: "", serial: "" }])}>
            <Plus />
            Add line
          </Button>
        </div>

        <div className="space-y-2">
          {lines.map((line, i) => (
            <div key={i} className="flex items-center gap-2">
              <input
                value={line.description}
                onChange={(e) => setLine(i, { description: e.target.value })}
                placeholder="Item / description"
                className={inputCls}
              />
              <input
                value={line.serial}
                onChange={(e) => setLine(i, { serial: e.target.value })}
                placeholder="Serial no."
                className={cn(inputCls, "sm:max-w-xs")}
              />
              <button
                type="button"
                onClick={() => setLines((l) => (l.length > 1 ? l.filter((_, idx) => idx !== i) : l))}
                className="grid size-9 shrink-0 place-items-center rounded-md text-muted transition-colors hover:bg-surface-sunken hover:text-danger"
                aria-label="Remove line"
              >
                <X className="size-4" />
              </button>
            </div>
          ))}
        </div>

        <Button className="mt-2 w-full sm:w-auto" onClick={submit} pending={submitting} disabled={!canSubmit}>
          Raise job card
        </Button>
      </Card>
    </FadeIn>
  );
}

const inputCls = cn(
  "w-full rounded-md border border-subtle bg-surface px-3 py-2 text-sm text-text placeholder:text-muted",
  "focus:border-strong focus:outline-none focus:ring-2 focus:ring-ring/25",
);

function Field({ label, value, onChange, placeholder }: { label: string; value: string; onChange: (v: string) => void; placeholder?: string }) {
  return (
    <label className="block space-y-1.5">
      <span className="block text-sm font-medium text-text">{label}</span>
      <textarea
        value={value}
        onChange={(e) => onChange(e.target.value)}
        placeholder={placeholder}
        rows={3}
        className={cn(inputCls, "resize-y")}
      />
    </label>
  );
}
