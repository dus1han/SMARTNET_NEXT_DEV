"use client";

/**
 * Customers — the first screen CLONED from the Users reference.
 *
 * This is the Phase 2 exit criterion cashed in (PHASE-2-PLAN.md, slice 5): the reference screen was
 * supposed to be the thing every other CRUD screen is copied from with "no new infrastructure". This
 * file is the test of that claim. It imports the same DataTable, the same useAppForm + zod, the same
 * applyServerErrors, the same useReason, the same History — and adds none of its own. If it had
 * needed to, that would have been the finding, and it would have gone back into components/.
 */

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ChevronDown, History as HistoryIcon, MoreHorizontal, Plus, SquarePen, Trash2, UserPlus, X } from "lucide-react";
import * as Menu from "@radix-ui/react-dropdown-menu";
import { useState } from "react";
import { z } from "zod";
import { ApiError } from "@/lib/api";
import {
  createCustomer,
  deleteCustomer,
  listCompanies,
  listCustomers,
  listProfitPercents,
  updateCustomer,
  type CompanySummary,
  type CustomerSummary,
  type ProfitPercent,
} from "@/lib/customers";
import type { CustomerContactDto } from "@smartnet/api-client";
import { cn } from "@/lib/cn";
import { PageHeader } from "@/components/shell/app-shell";
import { DataTable, type ColumnDef } from "@/components/data-table";
import { applyServerErrors, useAppForm, useReason } from "@/components/form";
import { History } from "@/components/history";

import {
  Badge,
  Button,
  Dialog,
  ErrorBanner,
  FadeIn,
  Input,
  Select,
  Textarea,
  toast,
} from "@/components/ui";

/** The one schema — it validates the form and types the payload (DEVELOPMENT.md §1). */
const customerSchema = z.object({
  name: z.string().min(1, "A name is required.").max(100),
  type: z.enum(["Company", "Individual"]),
  // contactPerson/email are no longer single fields — they are structured contacts (Phase 6, slice 4).
  address: z.string().max(100).optional(),
  phone: z.string().max(100).optional(),
  vatNumber: z.string().max(100).optional(),
  assignedCompanyId: z.coerce.number().int().nullable(),
  profitPercentId: z.coerce.number().int().nullable(),
  // Money as a plain number in the form; the API column is DECIMAL(18,4). Never negative — a
  // negative limit reads to the Phase 5 credit check as "always over".
  creditLimit: z.coerce.number().min(0, "A credit limit cannot be negative."),
});

type CustomerForm = z.infer<typeof customerSchema>;

const emptyCustomer: CustomerForm = {
  name: "",
  type: "Company",
  address: "",
  phone: "",
  vatNumber: "",
  assignedCompanyId: null,
  profitPercentId: null,
  creditLimit: 0,
};

export default function CustomersPage() {
  const queryClient = useQueryClient();
  const reason = useReason();

  const customers = useQuery({ queryKey: ["customers"], queryFn: listCustomers });
  const companies = useQuery({ queryKey: ["companies"], queryFn: listCompanies });
  const bands = useQuery({ queryKey: ["profit-percents"], queryFn: listProfitPercents });

  const [editing, setEditing] = useState<CustomerSummary | "new" | null>(null);
  const [inspecting, setInspecting] = useState<CustomerSummary | null>(null);

  const invalidate = () => queryClient.invalidateQueries({ queryKey: ["customers"] });

  const remove = useMutation({
    mutationFn: (v: { id: number; reason: string }) => deleteCustomer(v.id, v.reason),
    onSuccess: () => {
      toast.success("Customer removed.");
      void invalidate();
    },
    onError: (error: unknown) => toast.error(message(error)),
  });

  const columns: ColumnDef<CustomerSummary, unknown>[] = [
    {
      id: "customer",
      header: "Customer",
      accessorFn: (row) => row.code,
      // Sort by the code numerically — "C-2" before "C-10", which a plain string sort gets wrong.
      sortingFn: (a, b) => codeOrder(a.original.code) - codeOrder(b.original.code),
      cell: ({ row }) => {
        const customer = row.original;
        return (
          <div className="min-w-0">
            <p className="truncate font-medium text-text">{customer.name}</p>
            <p className="truncate text-xs text-muted">
              {customer.code}
              {customer.address ? ` · ${customer.address}` : ""}
            </p>
          </div>
        );
      },
    },
    {
      id: "type",
      header: "Type",
      accessorFn: (row) => row.type ?? "",
      cell: ({ row }) =>
        row.original.type ? (
          <Badge tone="neutral">{row.original.type}</Badge>
        ) : (
          <span className="text-muted">—</span>
        ),
    },
    {
      id: "contact",
      header: "Contacts",
      enableSorting: false,
      cell: ({ row }) => {
        const list = row.original.contacts;
        if (list.length === 0) return <span className="text-muted">—</span>;
        return (
          <Menu.Root>
            <Menu.Trigger asChild>
              <Button variant="ghost" size="sm" className="gap-1.5">
                {list.length} contact{list.length === 1 ? "" : "s"}
                <ChevronDown className="size-3.5 text-muted" aria-hidden />
              </Button>
            </Menu.Trigger>
            <Menu.Portal>
              <Menu.Content
                align="start"
                sideOffset={4}
                className="z-50 max-h-80 w-72 overflow-y-auto rounded-lg border border-subtle bg-surface p-1.5 shadow-lg data-[state=open]:animate-in data-[state=open]:fade-in-0 data-[state=open]:zoom-in-95"
              >
                {list.map((ct, i) => (
                  <div key={i} className="rounded-md px-2 py-1.5">
                    <div className="flex items-center justify-between gap-2">
                      <span className="truncate text-sm font-medium text-text">{ct.name || "—"}</span>
                      <Badge tone={ct.usage === NOTIFICATIONS_ONLY ? "neutral" : "success"}>
                        {ct.usage === NOTIFICATIONS_ONLY ? "Notify" : "Docs"}
                      </Badge>
                    </div>
                    {ct.phone && <div className="text-xs text-muted">{ct.phone}</div>}
                    {ct.email && <div className="truncate text-xs text-muted">{ct.email}</div>}
                  </div>
                ))}
              </Menu.Content>
            </Menu.Portal>
          </Menu.Root>
        );
      },
    },
    {
      id: "creditLimit",
      header: "Credit limit",
      meta: { align: "right" },
      accessorFn: (row) => row.creditLimit,
      cell: ({ row }) => (
        <span className="tabular text-text">
          {row.original.creditLimit > 0 ? formatMoney(row.original.creditLimit) : <span className="text-muted">—</span>}
        </span>
      ),
    },
    {
      id: "actions",
      header: "",
      enableSorting: false,
      cell: ({ row }) => {
        const customer = row.original;
        return (
          <Menu.Root>
            <Menu.Trigger asChild>
              <Button variant="ghost" size="icon" aria-label={`Actions for ${customer.name}`}>
                <MoreHorizontal />
              </Button>
            </Menu.Trigger>

            <Menu.Portal>
              <Menu.Content
                align="end"
                sideOffset={4}
                className="z-50 min-w-44 rounded-lg border border-subtle bg-surface p-1 shadow-lg data-[state=open]:animate-in data-[state=open]:fade-in-0 data-[state=open]:zoom-in-95"
              >
                <Menu.Item className={menuItem} onSelect={() => setEditing(customer)}>
                  <SquarePen className="size-4 text-muted" aria-hidden />
                  Edit
                </Menu.Item>

                <Menu.Item className={menuItem} onSelect={() => setInspecting(customer)}>
                  <HistoryIcon className="size-4 text-muted" aria-hidden />
                  History
                </Menu.Item>

                <Menu.Separator className="my-1 h-px bg-subtle" />

                <Menu.Item
                  className={cn(menuItem, "text-danger")}
                  onSelect={() =>
                    // Mandatory reason (AUDIT.md §5). The server rejects this without an
                    // X-Change-Reason header, and refuses outright if the customer has documents.
                    reason.ask({
                      title: `Remove ${customer.name}`,
                      description:
                        "The customer is hidden from the list. If it appears on any invoice or "
                        + "quotation, the server will refuse — its history would lose the name.",
                      confirmLabel: "Remove customer",
                      destructive: true,
                      onConfirm: (why) => remove.mutateAsync({ id: customer.id, reason: why }),
                    })
                  }
                >
                  <Trash2 className="size-4" aria-hidden />
                  Remove
                </Menu.Item>
              </Menu.Content>
            </Menu.Portal>
          </Menu.Root>
        );
      },
    },
  ];

  const loadError = (customers.error ?? companies.error ?? bands.error) as ApiError | null;

  return (
    <FadeIn className="space-y-6">
      <PageHeader
        title="Customers"
        description="Who you sell to. Shared across both companies — the associated entity is a default, not a wall."
        actions={
          <Button onClick={() => setEditing("new")}>
            <UserPlus />
            Add customer
          </Button>
        }
      />

      {loadError && (
        <ErrorBanner message={loadError.message} correlationId={loadError.correlationId} />
      )}

      <DataTable
        columns={columns}
        rows={customers.data}
        loading={customers.isPending}
        // Sorted by name on load, so the list is never in the API's arbitrary order. The header
        // shows the arrow, and the user can click any column — including the code — to re-sort.
        defaultSort={{ id: "customer" }}
        searchable={(c) => `${c.name} ${c.code} ${c.contacts.map((ct) => `${ct.name ?? ""} ${ct.phone ?? ""} ${ct.email ?? ""}`).join(" ")}`}
        searchPlaceholder="Search customers…"
        exportUrl="/api/customers/export"
        exportFilename="customers.xlsx"
        empty={{ title: "No customers yet", description: "Add one to get started." }}
      />

      <CustomerDialog
        target={editing}
        companies={companies.data ?? []}
        bands={bands.data ?? []}
        onClose={() => setEditing(null)}
        onSaved={invalidate}
      />

      <Dialog
        open={inspecting !== null}
        onOpenChange={(next) => !next && setInspecting(null)}
        size="lg"
        title={inspecting ? `History of ${inspecting.name}` : ""}
        description="Every change to this customer, and who made it."
      >
        {inspecting && <History entityType="Customer" entityId={inspecting.id} />}
      </Dialog>

      {reason.dialog}
    </FadeIn>
  );
}

const menuItem = cn(
  "flex cursor-pointer items-center gap-2 rounded-md px-2.5 py-2 text-sm outline-none",
  "transition-colors duration-150 data-[highlighted]:bg-surface-sunken",
);

function CustomerDialog({ target, companies, bands, onClose, onSaved }: {
  target: CustomerSummary | "new" | null;
  companies: CompanySummary[];
  bands: ProfitPercent[];
  onClose: () => void;
  onSaved: () => void;
}) {
  const editing = target !== null && target !== "new" ? target : null;
  const form = useAppForm<CustomerForm>(customerSchema, emptyCustomer);
  const [banner, setBanner] = useState<string[]>([]);
  const [loaded, setLoaded] = useState<number | "new" | null>(null);
  const [contacts, setContacts] = useState<ContactRow[]>([]);

  // Sync the form to whichever customer was opened, during render rather than in an effect.
  const key = editing?.id ?? (target === "new" ? "new" : null);
  if (target !== null && loaded !== key) {
    form.reset(editing ? toForm(editing) : emptyCustomer);
    setContacts(editing ? editing.contacts.map(toContactRow) : []);
    setBanner([]);
    setLoaded(key);
  }

  // An Individual customer is a person: as soon as the type is Individual, the first contact takes the
  // customer's own name (and tracks it as the name is typed). Synced during render — guarded so it converges,
  // the same pattern as the form reset above (AGENTS.md: sync during render, not in an effect).
  const watchType = form.watch("type");
  const watchName = (form.watch("name") ?? "").trim();
  if (watchType === "Individual" && watchName && (contacts.length === 0 || contacts[0].name !== watchName)) {
    setContacts(
      contacts.length === 0
        ? [{ ...blankContactRow, name: watchName }]
        : contacts.map((r, i) => (i === 0 ? { ...r, name: watchName } : r)),
    );
  }

  const save = useMutation({
    mutationFn: async (values: CustomerForm): Promise<void> => {
      const request = { ...toRequest(values), contacts: toContactDtos(contacts) };
      if (editing) await updateCustomer(editing.id, request);
      else await createCustomer(request);
    },
    onSuccess: () => {
      toast.success(editing ? "Customer saved." : "Customer created.");
      onSaved();
      onClose();
    },
    onError: (error: unknown) => setBanner(applyServerErrors(form, error)),
  });

  return (
    <Dialog
      open={target !== null}
      onOpenChange={(next) => !next && onClose()}
      size="lg"
      title={editing ? `Edit ${editing.name}` : "Add a customer"}
      description={
        editing
          ? `Code ${editing.code}. The code does not change.`
          : "The code is allocated by the server, from the same sequence the old system uses."
      }
      footer={
        <>
          <Button variant="ghost" onClick={onClose}>
            Cancel
          </Button>
          <Button
            pending={save.isPending}
            onClick={form.handleSubmit((values) => save.mutate(values))}
          >
            {editing ? "Save changes" : "Create customer"}
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        {banner.map((text) => (
          <ErrorBanner key={text} message={text} />
        ))}

        <div className="grid gap-4 sm:grid-cols-2">
          <Input
            label="Name"
            required
            error={form.formState.errors.name?.message}
            {...form.register("name")}
          />

          <Select label="Type" error={form.formState.errors.type?.message} {...form.register("type")}>
            <option value="Company">Company</option>
            <option value="Individual">Individual</option>
          </Select>

          {/* The company-level phone is no longer collected — a contact person's phone is enough. The
              existing value is preserved (kept in the form state, not wiped) and migrated onto a contact. */}
          <Input
            label="VAT number"
            error={form.formState.errors.vatNumber?.message}
            {...form.register("vatNumber")}
          />
        </div>

        <Textarea
          label="Address"
          error={form.formState.errors.address?.message}
          {...form.register("address")}
        />

        {/* Structured contacts (Phase 6, slice 4) — the real rows behind the legacy ;-separated columns,
            which the server dual-writes from these on save. */}
        <ContactsEditor contacts={contacts} onChange={setContacts} />

        <div className="grid gap-4 sm:grid-cols-3">
          <Select
            label="Associated with"
            hint="A default, not a restriction."
            {...form.register("assignedCompanyId")}
          >
            <option value="">— none —</option>
            {companies.map((c) => (
              <option key={c.id} value={c.id}>
                {c.name}
              </option>
            ))}
          </Select>

          <Select label="Margin band" {...form.register("profitPercentId")}>
            <option value="">— none —</option>
            {bands.map((b) => (
              <option key={b.id} value={b.id}>
                {b.name}%
              </option>
            ))}
          </Select>

          <Input
            label="Credit limit"
            type="number"
            inputMode="decimal"
            step="0.01"
            error={form.formState.errors.creditLimit?.message}
            {...form.register("creditLimit")}
          />
        </div>
      </div>
    </Dialog>
  );
}

function toForm(c: CustomerSummary): CustomerForm {
  return {
    name: c.name,
    type: c.type === "Individual" ? "Individual" : "Company",
    address: c.address ?? "",
    phone: c.phone ?? "",
    vatNumber: c.vatNumber ?? "",
    assignedCompanyId: c.assignedCompanyId ?? null,
    profitPercentId: c.profitPercentId ?? null,
    creditLimit: c.creditLimit,
  };
}

// --- Structured contacts (Phase 6, slice 4) -----------------------------------------------------

// A contact is either printed on documents (and notified) or notified only — see ContactUsage on the API.
const DOCUMENTS_AND_NOTIFICATIONS = "DocumentsAndNotifications";
const NOTIFICATIONS_ONLY = "NotificationsOnly";

interface ContactRow { name: string; phone: string; email: string; usage: string }
const blankContactRow: ContactRow = { name: "", phone: "", email: "", usage: DOCUMENTS_AND_NOTIFICATIONS };

function toContactRow(c: CustomerContactDto): ContactRow {
  return { name: c.name ?? "", phone: c.phone ?? "", email: c.email ?? "", usage: c.usage };
}


/** The rows the user filled, as DTOs — blank rows dropped. Id 0: the server allocates. */
function toContactDtos(rows: ContactRow[]): CustomerContactDto[] {
  const filled = rows.filter((r) => r.name.trim() || r.email.trim() || r.phone.trim());
  const blankToNull = (s: string) => (s.trim() !== "" ? s.trim() : null);
  return filled.map((r) => ({
    id: 0,
    name: blankToNull(r.name),
    phone: blankToNull(r.phone),
    email: blankToNull(r.email),
    usage: r.usage === NOTIFICATIONS_ONLY ? NOTIFICATIONS_ONLY : DOCUMENTS_AND_NOTIFICATIONS,
  }));
}

const contactInput = cn(
  "min-w-0 flex-1 rounded-md border border-subtle bg-surface px-2.5 py-1.5 text-sm text-text placeholder:text-muted",
  "focus:border-strong focus:outline-none focus:ring-2 focus:ring-ring/25",
);

function ContactsEditor({ contacts, onChange }: { contacts: ContactRow[]; onChange: (contacts: ContactRow[]) => void }) {
  const set = (i: number, patch: Partial<ContactRow>) => onChange(contacts.map((c, idx) => (idx === i ? { ...c, ...patch } : c)));

  return (
    <div className="space-y-2">
      <div className="flex items-center justify-between">
        <label className="text-sm font-medium text-text">Contacts</label>
        <Button
          type="button"
          variant="secondary"
          size="sm"
          onClick={() => onChange([...contacts, { ...blankContactRow }])}
        >
          <Plus />
          Add contact
        </Button>
      </div>

      {contacts.length === 0 && (
        <p className="text-sm text-muted">No contacts yet. Add one — choose whether it appears on documents or is for notifications only.</p>
      )}

      {contacts.map((c, i) => (
        <div key={i} className="flex flex-wrap items-center gap-2">
          <input placeholder="Name" value={c.name} onChange={(e) => set(i, { name: e.target.value })} className={contactInput} />
          <input type="email" placeholder="Email" value={c.email} onChange={(e) => set(i, { email: e.target.value })} className={contactInput} />
          <input placeholder="Phone" value={c.phone} onChange={(e) => set(i, { phone: e.target.value })} className={cn(contactInput, "sm:max-w-[9rem]")} />
          <select
            value={c.usage}
            onChange={(e) => set(i, { usage: e.target.value })}
            className={cn(contactInput, "shrink-0 sm:max-w-52")}
            aria-label="Contact usage"
            title="Where this contact is used"
          >
            <option value={DOCUMENTS_AND_NOTIFICATIONS}>Documents &amp; notifications</option>
            <option value={NOTIFICATIONS_ONLY}>Notifications only</option>
          </select>
          <button
            type="button"
            onClick={() => onChange(contacts.filter((_, idx) => idx !== i))}
            className="grid size-8 shrink-0 place-items-center rounded-md text-muted transition-colors hover:bg-surface-sunken hover:text-danger"
            aria-label="Remove contact"
          >
            <X className="size-4" />
          </button>
        </div>
      ))}
    </div>
  );
}

function toRequest(values: CustomerForm) {
  const blankToNull = (s?: string) => (s && s.trim() !== "" ? s.trim() : null);

  return {
    name: values.name.trim(),
    type: values.type,
    // contactPerson/email are derived from the structured contacts server-side; sent null here.
    contactPerson: null,
    address: blankToNull(values.address),
    phone: blankToNull(values.phone),
    email: null,
    vatNumber: blankToNull(values.vatNumber),
    // An empty <select> coerces to 0 via z.coerce.number; 0 is not a company, so it is null.
    assignedCompanyId: values.assignedCompanyId ? values.assignedCompanyId : null,
    profitPercentId: values.profitPercentId ? values.profitPercentId : null,
    creditLimit: values.creditLimit,
  };
}

/**
 * The numeric part of a customer code, for natural ordering.
 *
 * "C-2" must sort before "C-10". A plain string comparison puts "C-10" first, because "1" < "2" —
 * which is exactly the jumble this is fixing. A code with no number in it (should not happen) sorts
 * to the end rather than throwing.
 */
function codeOrder(code: string): number {
  const digits = code.replace(/\D/g, "");
  return digits === "" ? Number.MAX_SAFE_INTEGER : Number(digits);
}

/** Money for display: grouped, two decimals. The API sends a decimal; Excel export sends the number. */
function formatMoney(value: number): string {
  return value.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}

function message(error: unknown) {
  return error instanceof ApiError ? error.message : "That did not work.";
}
