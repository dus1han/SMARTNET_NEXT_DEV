"use client";

/**
 * Suppliers — the second screen CLONED from the Users reference, and the measurement Phase 3 slice 3
 * is for. Customers proved the pattern works; this proves it is cheap. It is the Customers page with
 * the customer-only fields removed — no type, no credit limit, no margin band, no associated company —
 * and nothing of its own added: the same DataTable, useAppForm + zod, applyServerErrors, useReason and
 * History. If it had needed one new component, that would have been the finding.
 */

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { History as HistoryIcon, MoreHorizontal, SquarePen, Trash2, UserPlus } from "lucide-react";
import * as Menu from "@radix-ui/react-dropdown-menu";
import { useState } from "react";
import { z } from "zod";
import { ApiError } from "@/lib/api";
import {
  createSupplier,
  deleteSupplier,
  listSuppliers,
  updateSupplier,
  type SupplierSummary,
} from "@/lib/suppliers";
import { cn } from "@/lib/cn";
import { PageHeader } from "@/components/shell/app-shell";
import { DataTable, type ColumnDef } from "@/components/data-table";
import { applyServerErrors, useAppForm, useReason } from "@/components/form";
import { History } from "@/components/history";
import { Button, Dialog, ErrorBanner, FadeIn, Input, Textarea, toast } from "@/components/ui";

/** The one schema — it validates the form and types the payload (DEVELOPMENT.md §1). */
const supplierSchema = z.object({
  name: z.string().min(1, "A name is required.").max(100),
  contactPerson: z.string().max(100).optional(),
  address: z.string().max(100).optional(),
  phone: z.string().max(100).optional(),
  email: z.string().email("That is not a valid email address.").or(z.literal("")).optional(),
  vatNumber: z.string().max(100).optional(),
});

type SupplierForm = z.infer<typeof supplierSchema>;

const emptySupplier: SupplierForm = {
  name: "",
  contactPerson: "",
  address: "",
  phone: "",
  email: "",
  vatNumber: "",
};

export default function SuppliersPage() {
  const queryClient = useQueryClient();
  const reason = useReason();

  const suppliers = useQuery({ queryKey: ["suppliers"], queryFn: listSuppliers });

  const [editing, setEditing] = useState<SupplierSummary | "new" | null>(null);
  const [inspecting, setInspecting] = useState<SupplierSummary | null>(null);

  const invalidate = () => queryClient.invalidateQueries({ queryKey: ["suppliers"] });

  const remove = useMutation({
    mutationFn: (v: { id: number; reason: string }) => deleteSupplier(v.id, v.reason),
    onSuccess: () => {
      toast.success("Supplier removed.");
      void invalidate();
    },
    onError: (error: unknown) => toast.error(message(error)),
  });

  const columns: ColumnDef<SupplierSummary, unknown>[] = [
    {
      id: "supplier",
      header: "Supplier",
      accessorFn: (row) => row.code,
      // Sort by the code numerically — "S-2" before "S-10", which a plain string sort gets wrong.
      sortingFn: (a, b) => codeOrder(a.original.code) - codeOrder(b.original.code),
      cell: ({ row }) => {
        const supplier = row.original;
        return (
          <div className="min-w-0">
            <p className="truncate font-medium text-text">{supplier.name}</p>
            <p className="truncate text-xs text-muted">
              {supplier.code}
              {supplier.contactPerson ? ` · ${supplier.contactPerson}` : ""}
            </p>
          </div>
        );
      },
    },
    {
      id: "contact",
      header: "Contact",
      accessorFn: (row) => row.phone ?? "",
      cell: ({ row }) => (
        <div className="min-w-0 text-sm">
          <p className="truncate text-text">{row.original.phone || "—"}</p>
          <p className="truncate text-xs text-muted">{row.original.email}</p>
        </div>
      ),
    },
    {
      id: "vatNumber",
      header: "VAT number",
      accessorFn: (row) => row.vatNumber ?? "",
      cell: ({ row }) =>
        row.original.vatNumber ? (
          <span className="tabular text-sm text-text">{row.original.vatNumber}</span>
        ) : (
          <span className="text-muted">—</span>
        ),
    },
    {
      id: "actions",
      header: "",
      enableSorting: false,
      cell: ({ row }) => {
        const supplier = row.original;
        return (
          <Menu.Root>
            <Menu.Trigger asChild>
              <Button variant="ghost" size="icon" aria-label={`Actions for ${supplier.name}`}>
                <MoreHorizontal />
              </Button>
            </Menu.Trigger>

            <Menu.Portal>
              <Menu.Content
                align="end"
                sideOffset={4}
                className="z-50 min-w-44 rounded-lg border border-subtle bg-surface p-1 shadow-lg data-[state=open]:animate-in data-[state=open]:fade-in-0 data-[state=open]:zoom-in-95"
              >
                <Menu.Item className={menuItem} onSelect={() => setEditing(supplier)}>
                  <SquarePen className="size-4 text-muted" aria-hidden />
                  Edit
                </Menu.Item>

                <Menu.Item className={menuItem} onSelect={() => setInspecting(supplier)}>
                  <HistoryIcon className="size-4 text-muted" aria-hidden />
                  History
                </Menu.Item>

                <Menu.Separator className="my-1 h-px bg-subtle" />

                <Menu.Item
                  className={cn(menuItem, "text-danger")}
                  onSelect={() =>
                    // Mandatory reason (AUDIT.md §5). The server rejects this without an
                    // X-Change-Reason header, and refuses outright if the supplier has documents.
                    reason.ask({
                      title: `Remove ${supplier.name}`,
                      description:
                        "The supplier is hidden from the list. If it appears on any purchase order or "
                        + "supplier invoice, the server will refuse — its history would lose the name.",
                      confirmLabel: "Remove supplier",
                      destructive: true,
                      onConfirm: (why) => remove.mutateAsync({ id: supplier.id, reason: why }),
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

  const loadError = suppliers.error as ApiError | null;

  return (
    <FadeIn className="space-y-6">
      <PageHeader
        title="Suppliers"
        description="Who you buy from. Shared across both companies, like customers."
        actions={
          <Button onClick={() => setEditing("new")}>
            <UserPlus />
            Add supplier
          </Button>
        }
      />

      {loadError && (
        <ErrorBanner message={loadError.message} correlationId={loadError.correlationId} />
      )}

      <DataTable
        columns={columns}
        rows={suppliers.data}
        loading={suppliers.isPending}
        defaultSort={{ id: "supplier" }}
        searchable={(s) => `${s.name} ${s.code} ${s.contactPerson ?? ""} ${s.phone ?? ""} ${s.email ?? ""}`}
        searchPlaceholder="Search suppliers…"
        exportUrl="/api/suppliers/export"
        exportFilename="suppliers.xlsx"
        empty={{ title: "No suppliers yet", description: "Add one to get started." }}
      />

      <SupplierDialog
        target={editing}
        onClose={() => setEditing(null)}
        onSaved={invalidate}
      />

      <Dialog
        open={inspecting !== null}
        onOpenChange={(next) => !next && setInspecting(null)}
        size="lg"
        title={inspecting ? `History of ${inspecting.name}` : ""}
        description="Every change to this supplier, and who made it."
      >
        {inspecting && <History entityType="Supplier" entityId={inspecting.id} />}
      </Dialog>

      {reason.dialog}
    </FadeIn>
  );
}

const menuItem = cn(
  "flex cursor-pointer items-center gap-2 rounded-md px-2.5 py-2 text-sm outline-none",
  "transition-colors duration-150 data-[highlighted]:bg-surface-sunken",
);

function SupplierDialog({ target, onClose, onSaved }: {
  target: SupplierSummary | "new" | null;
  onClose: () => void;
  onSaved: () => void;
}) {
  const editing = target !== null && target !== "new" ? target : null;
  const form = useAppForm<SupplierForm>(supplierSchema, emptySupplier);
  const [banner, setBanner] = useState<string[]>([]);
  const [loaded, setLoaded] = useState<number | "new" | null>(null);

  // Sync the form to whichever supplier was opened, during render rather than in an effect.
  const key = editing?.id ?? (target === "new" ? "new" : null);
  if (target !== null && loaded !== key) {
    form.reset(editing ? toForm(editing) : emptySupplier);
    setBanner([]);
    setLoaded(key);
  }

  const save = useMutation({
    mutationFn: async (values: SupplierForm): Promise<void> => {
      // expectedRowVersion: the version this form was opened on, so a concurrent edit is refused
      // rather than silently overwriting whoever saved first.
      if (editing) {
        await updateSupplier(editing.id, { ...toRequest(values), expectedRowVersion: editing.rowVersion });
      } else {
        await createSupplier(toRequest(values));
      }
    },
    onSuccess: () => {
      toast.success(editing ? "Supplier saved." : "Supplier created.");
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
      title={editing ? `Edit ${editing.name}` : "Add a supplier"}
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
            {editing ? "Save changes" : "Create supplier"}
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

          <Input
            label="Contact person"
            error={form.formState.errors.contactPerson?.message}
            {...form.register("contactPerson")}
          />

          <Input
            label="Phone"
            error={form.formState.errors.phone?.message}
            {...form.register("phone")}
          />

          <Input
            label="Email"
            type="email"
            error={form.formState.errors.email?.message}
            {...form.register("email")}
          />

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
      </div>
    </Dialog>
  );
}

function toForm(s: SupplierSummary): SupplierForm {
  return {
    name: s.name,
    contactPerson: s.contactPerson ?? "",
    address: s.address ?? "",
    phone: s.phone ?? "",
    email: s.email ?? "",
    vatNumber: s.vatNumber ?? "",
  };
}

function toRequest(values: SupplierForm) {
  const blankToNull = (v?: string) => (v && v.trim() !== "" ? v.trim() : null);

  return {
    name: values.name.trim(),
    contactPerson: blankToNull(values.contactPerson),
    address: blankToNull(values.address),
    phone: blankToNull(values.phone),
    email: blankToNull(values.email),
    vatNumber: blankToNull(values.vatNumber),
  };
}

/**
 * The numeric part of a supplier code, for natural ordering. "S-2" must sort before "S-10"; a plain
 * string comparison puts "S-10" first. A code with no number (should not happen) sorts to the end.
 */
function codeOrder(code: string): number {
  const digits = code.replace(/\D/g, "");
  return digits === "" ? Number.MAX_SAFE_INTEGER : Number(digits);
}

function message(error: unknown) {
  return error instanceof ApiError ? error.message : "That did not work.";
}
