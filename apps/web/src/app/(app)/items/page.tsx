"use client";

/**
 * Items — the catalogue, and the stock ledger behind it.
 *
 * The catalogue half is the third screen CLONED from the Users reference (after Customers and
 * Suppliers): same DataTable, useAppForm + zod, applyServerErrors, useReason, History. The stock half
 * is the one genuinely new thing in Phase 3 — a balance that is never stored, only derived from an
 * append-only movement ledger (ISSUES B3). An adjustment adds a movement; it never edits a number.
 */

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Boxes, History as HistoryIcon, MoreHorizontal, Package, SquarePen, Trash2 } from "lucide-react";
import * as Menu from "@radix-ui/react-dropdown-menu";
import { useState } from "react";
import { z } from "zod";
import { ApiError } from "@/lib/api";
import { formatInstant } from "@/lib/time";
import {
  adjustStock,
  createItem,
  deleteItem,
  getItemStock,
  listItems,
  updateItem,
  type ItemSummary,
  type SaveItemRequest,
} from "@/lib/items";
import { cn } from "@/lib/cn";
import { PageHeader } from "@/components/shell/app-shell";
import { DataTable, type ColumnDef } from "@/components/data-table";
import { applyServerErrors, useAppForm, useReason } from "@/components/form";
import { History } from "@/components/history";
import { Badge, Button, Checkbox, Dialog, ErrorBanner, FadeIn, Input, Spinner, toast } from "@/components/ui";

/**
 * The one schema — validates the form and types the payload (DEVELOPMENT.md §1).
 *
 * The numeric fields are optional, so the form model keeps them as strings: an empty string is "not
 * set" (→ null), which a coerced number cannot express — 0 is a real price, blank is not. The
 * string→number conversion is done explicitly in toRequest, which is why there are no zod transforms
 * here and the input and output shapes stay identical (the form abstraction types fields by output).
 */
const itemSchema = z.object({
  name: z.string().min(1, "A name is required.").max(100),
  unit: z.string().max(32),
  sellingPrice: optionalAmount("A price cannot be negative."),
  cost: optionalAmount("A cost cannot be negative."),
  reorderLevel: optionalAmount("A reorder level cannot be negative."),
});

type ItemForm = z.infer<typeof itemSchema>;

const emptyItem: ItemForm = {
  name: "",
  unit: "",
  sellingPrice: "",
  cost: "",
  reorderLevel: "",
};

const blank = (v: string) => v.trim() === "";
const numeric = (v: string) => Number.isFinite(Number(v));

/** An optional, non-negative amount held as a string: blank means "not set". */
function optionalAmount(negativeMessage: string) {
  return z.string().refine((v) => blank(v) || (numeric(v) && Number(v) >= 0), negativeMessage);
}

export default function ItemsPage() {
  const queryClient = useQueryClient();
  const reason = useReason();

  const items = useQuery({ queryKey: ["items"], queryFn: listItems });

  const [editing, setEditing] = useState<ItemSummary | "new" | null>(null);
  const [stockOf, setStockOf] = useState<ItemSummary | null>(null);
  const [inspecting, setInspecting] = useState<ItemSummary | null>(null);
  const [belowReorderOnly, setBelowReorderOnly] = useState(false);

  const invalidate = () => queryClient.invalidateQueries({ queryKey: ["items"] });

  const remove = useMutation({
    mutationFn: (v: { id: number; reason: string }) => deleteItem(v.id, v.reason),
    onSuccess: () => {
      toast.success("Item removed.");
      void invalidate();
    },
    onError: (error: unknown) => toast.error(message(error)),
  });

  const columns: ColumnDef<ItemSummary, unknown>[] = [
    {
      id: "item",
      header: "Item",
      accessorFn: (row) => row.code,
      sortingFn: (a, b) => codeOrder(a.original.code) - codeOrder(b.original.code),
      cell: ({ row }) => {
        const item = row.original;
        return (
          <div className="min-w-0">
            <p className="truncate font-medium text-text">{item.name}</p>
            <p className="truncate text-xs text-muted">
              {item.code}
              {item.unit ? ` · ${item.unit}` : ""}
            </p>
          </div>
        );
      },
    },
    {
      id: "price",
      header: "Price",
      meta: { align: "right" },
      accessorFn: (row) => row.sellingPrice ?? -1,
      cell: ({ row }) => (
        <span className="tabular text-text">
          {row.original.sellingPrice != null
            ? formatMoney(row.original.sellingPrice)
            : <span className="text-muted">—</span>}
        </span>
      ),
    },
    {
      id: "stock",
      header: "In stock",
      meta: { align: "center" },
      accessorFn: (row) => row.stockBalance,
      cell: ({ row }) => {
        const item = row.original;
        return (
          <div className="flex items-center gap-2">
            <span className="tabular text-text">{formatQuantity(item.stockBalance)}</span>
            {item.belowReorder && <Badge tone="warning">Reorder</Badge>}
          </div>
        );
      },
    },
    {
      id: "actions",
      header: "",
      enableSorting: false,
      cell: ({ row }) => {
        const item = row.original;
        return (
          <Menu.Root>
            <Menu.Trigger asChild>
              <Button variant="ghost" size="icon" aria-label={`Actions for ${item.name}`}>
                <MoreHorizontal />
              </Button>
            </Menu.Trigger>

            <Menu.Portal>
              <Menu.Content
                align="end"
                sideOffset={4}
                className="z-50 min-w-44 rounded-lg border border-subtle bg-surface p-1 shadow-lg data-[state=open]:animate-in data-[state=open]:fade-in-0 data-[state=open]:zoom-in-95"
              >
                <Menu.Item className={menuItem} onSelect={() => setEditing(item)}>
                  <SquarePen className="size-4 text-muted" aria-hidden />
                  Edit
                </Menu.Item>

                <Menu.Item className={menuItem} onSelect={() => setStockOf(item)}>
                  <Boxes className="size-4 text-muted" aria-hidden />
                  Stock
                </Menu.Item>

                <Menu.Item className={menuItem} onSelect={() => setInspecting(item)}>
                  <HistoryIcon className="size-4 text-muted" aria-hidden />
                  History
                </Menu.Item>

                <Menu.Separator className="my-1 h-px bg-subtle" />

                <Menu.Item
                  className={cn(menuItem, "text-danger")}
                  onSelect={() =>
                    reason.ask({
                      title: `Remove ${item.name}`,
                      description:
                        "The item is hidden from the catalogue. If it has any stock history the server "
                        + "will refuse — adjust its stock to zero instead.",
                      confirmLabel: "Remove item",
                      destructive: true,
                      onConfirm: (why) => remove.mutateAsync({ id: item.id, reason: why }),
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

  const loadError = items.error as ApiError | null;

  // The "below reorder" filter — the whole point of the reorder-level field. Client-side, on the
  // rows already loaded, so it needs no new DataTable feature (Phase 2's exit criterion holds).
  const rows = belowReorderOnly ? items.data?.filter((i) => i.belowReorder) : items.data;

  return (
    <FadeIn className="space-y-6">
      <PageHeader
        title="Items"
        description="The catalogue. Half the documents are supposed to come out of it — so it is only ever as complete as the cheapest way to add to it."
        actions={
          <Button onClick={() => setEditing("new")}>
            <Package />
            Add item
          </Button>
        }
      />

      {loadError && <ErrorBanner message={loadError.message} correlationId={loadError.correlationId} />}

      <Checkbox
        label="Below reorder level only"
        checked={belowReorderOnly}
        onChange={(e) => setBelowReorderOnly(e.currentTarget.checked)}
      />

      <DataTable
        columns={columns}
        rows={rows}
        loading={items.isPending}
        defaultSort={{ id: "item" }}
        searchable={(i) => `${i.name} ${i.code} ${i.unit ?? ""}`}
        searchPlaceholder="Search items…"
        exportUrl="/api/items/export"
        exportFilename="items.xlsx"
        empty={{
          title: belowReorderOnly ? "Nothing below reorder" : "No items yet",
          description: belowReorderOnly ? "Every item is above its reorder level." : "Add one to get started.",
        }}
      />

      <ItemDialog target={editing} onClose={() => setEditing(null)} onSaved={invalidate} />

      <Dialog
        open={stockOf !== null}
        onOpenChange={(next) => !next && setStockOf(null)}
        size="lg"
        title={stockOf ? `Stock — ${stockOf.name}` : ""}
        description="The balance is the sum of every movement. A correction is a new movement, never an edit."
      >
        {stockOf && <StockPanel item={stockOf} onChanged={invalidate} />}
      </Dialog>

      <Dialog
        open={inspecting !== null}
        onOpenChange={(next) => !next && setInspecting(null)}
        size="lg"
        title={inspecting ? `History of ${inspecting.name}` : ""}
        description="Every change to this item, and who made it."
      >
        {inspecting && <History entityType="Item" entityId={inspecting.id} />}
      </Dialog>

      {reason.dialog}
    </FadeIn>
  );
}

const menuItem = cn(
  "flex cursor-pointer items-center gap-2 rounded-md px-2.5 py-2 text-sm outline-none",
  "transition-colors duration-150 data-[highlighted]:bg-surface-sunken",
);

// --- Catalogue form --------------------------------------------------------------------------

function ItemDialog({ target, onClose, onSaved }: {
  target: ItemSummary | "new" | null;
  onClose: () => void;
  onSaved: () => void;
}) {
  const editing = target !== null && target !== "new" ? target : null;
  const form = useAppForm<ItemForm>(itemSchema, emptyItem);
  const [banner, setBanner] = useState<string[]>([]);
  const [loaded, setLoaded] = useState<number | "new" | null>(null);

  const key = editing?.id ?? (target === "new" ? "new" : null);
  if (target !== null && loaded !== key) {
    form.reset(editing ? toForm(editing) : emptyItem);
    setBanner([]);
    setLoaded(key);
  }

  const save = useMutation({
    mutationFn: async (values: ItemForm): Promise<void> => {
      const payload = toRequest(values);
      if (editing) await updateItem(editing.id, payload);
      else await createItem(payload);
    },
    onSuccess: () => {
      toast.success(editing ? "Item saved." : "Item created.");
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
      title={editing ? `Edit ${editing.name}` : "Add an item"}
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
          <Button pending={save.isPending} onClick={form.handleSubmit((values) => save.mutate(values))}>
            {editing ? "Save changes" : "Create item"}
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        {banner.map((text) => (
          <ErrorBanner key={text} message={text} />
        ))}

        <div className="grid gap-4 sm:grid-cols-2">
          <Input label="Name" required error={form.formState.errors.name?.message} {...form.register("name")} />
          <Input label="Unit" hint="pcs, box, m…" error={form.formState.errors.unit?.message} {...form.register("unit")} />
        </div>

        <div className="grid gap-4 sm:grid-cols-2">
          <Input
            label="Selling price"
            type="number"
            inputMode="decimal"
            step="0.01"
            error={form.formState.errors.sellingPrice?.message}
            {...form.register("sellingPrice")}
          />
          <Input
            label="Cost"
            type="number"
            inputMode="decimal"
            step="0.01"
            error={form.formState.errors.cost?.message}
            {...form.register("cost")}
          />
        </div>

        <div className="grid gap-4 sm:grid-cols-2">
          <Input
            label="Reorder level"
            hint="Shows the item on the reorder list at or below this."
            type="number"
            inputMode="decimal"
            step="0.01"
            error={form.formState.errors.reorderLevel?.message}
            {...form.register("reorderLevel")}
          />
        </div>
      </div>
    </Dialog>
  );
}

// --- Stock panel -----------------------------------------------------------------------------

function StockPanel({ item, onChanged }: { item: ItemSummary; onChanged: () => void }) {
  const queryClient = useQueryClient();
  const stock = useQuery({ queryKey: ["item-stock", item.id], queryFn: () => getItemStock(item.id) });

  const [quantity, setQuantity] = useState("");
  const [why, setWhy] = useState("");
  const [error, setError] = useState<string | null>(null);

  const adjust = useMutation({
    mutationFn: () => adjustStock(item.id, { quantity: Number(quantity), reason: why }),
    onSuccess: () => {
      toast.success("Stock adjusted.");
      setQuantity("");
      setWhy("");
      setError(null);
      void queryClient.invalidateQueries({ queryKey: ["item-stock", item.id] });
      onChanged(); // the list's balance changed too
    },
    onError: (e: unknown) => setError(message(e)),
  });

  function submit() {
    const q = Number(quantity);
    if (!quantity || Number.isNaN(q) || q === 0) return setError("Enter a quantity to add or remove.");
    if (!why.trim()) return setError("A stock adjustment needs a reason.");
    adjust.mutate();
  }

  if (stock.isPending) {
    return (
      <div className="flex justify-center py-10">
        <Spinner />
      </div>
    );
  }

  if (stock.error) {
    const e = stock.error as ApiError;
    return <ErrorBanner message={e.message} correlationId={e.correlationId} />;
  }

  const data = stock.data!;
  const ledger = [...data.movements].reverse(); // newest first for reading

  return (
    <div className="space-y-5">
      <div className="flex items-baseline gap-3 rounded-xl border border-subtle bg-surface-sunken px-4 py-3">
        <span className="text-sm text-muted">Balance</span>
        <span className="text-2xl font-semibold tabular text-text">{formatQuantity(data.balance)}</span>
        {data.reorderLevel != null && data.balance <= data.reorderLevel && (
          <Badge tone="warning">At or below reorder ({formatQuantity(data.reorderLevel)})</Badge>
        )}
      </div>

      {/* Record an adjustment — a new movement. Positive adds, negative removes. */}
      <div className="rounded-xl border border-subtle p-4">
        <p className="mb-3 text-sm font-medium text-text">Adjust stock</p>
        {error && <div className="mb-3"><ErrorBanner message={error} /></div>}
        <div className="grid gap-3 sm:grid-cols-[8rem_1fr_auto] sm:items-end">
          <Input
            label="Quantity"
            type="number"
            inputMode="decimal"
            step="0.01"
            placeholder="+10 / −3"
            value={quantity}
            onChange={(e) => setQuantity(e.target.value)}
          />
          <Input
            label="Reason"
            placeholder="Stock count, breakage, opening balance…"
            value={why}
            onChange={(e) => setWhy(e.target.value)}
          />
          <Button pending={adjust.isPending} onClick={submit}>
            Record
          </Button>
        </div>
      </div>

      {/* The ledger — the whole reason a balance can be trusted. */}
      <div>
        <p className="mb-2 text-sm font-medium text-text">Movements</p>
        {ledger.length === 0 ? (
          <p className="rounded-lg border border-dashed border-subtle px-4 py-6 text-center text-sm text-muted">
            No movements yet. The first adjustment starts the ledger.
          </p>
        ) : (
          <ul className="divide-y divide-subtle rounded-lg border border-subtle">
            {ledger.map((m) => (
              <li key={m.id} className="flex items-center justify-between gap-3 px-4 py-2.5 text-sm">
                <div className="min-w-0">
                  <p className="truncate text-text">{m.reason || m.type}</p>
                  <p className="text-xs text-muted">{formatDate(m.occurredAt)}</p>
                </div>
                <div className="flex items-center gap-4 tabular">
                  <span className={cn("font-medium", m.quantity < 0 ? "text-danger" : "text-success-text")}>
                    {m.quantity > 0 ? "+" : ""}
                    {formatQuantity(m.quantity)}
                  </span>
                  <span className="w-16 text-right text-muted">{formatQuantity(m.balanceAfter)}</span>
                </div>
              </li>
            ))}
          </ul>
        )}
      </div>

      {/* The legacy receipt batches, shown for reference. Read-only: nothing here writes them. */}
      {data.batches.length > 0 && (
        <div>
          <p className="mb-2 text-sm font-medium text-text">Legacy batches</p>
          <ul className="divide-y divide-subtle rounded-lg border border-subtle">
            {data.batches.map((b) => (
              <li key={b.id} className="flex items-center justify-between px-4 py-2 text-sm text-muted">
                <span>{b.inDate ?? "—"}</span>
                <span className="tabular">qty {formatQuantity(b.quantity ?? 0)} · bal {formatQuantity(b.balance ?? 0)}</span>
              </li>
            ))}
          </ul>
        </div>
      )}
    </div>
  );
}

// --- mapping & formatting --------------------------------------------------------------------

function toForm(i: ItemSummary): ItemForm {
  const s = (n: number | null | undefined) => (n == null ? "" : String(n));
  return {
    name: i.name,
    unit: i.unit ?? "",
    sellingPrice: s(i.sellingPrice),
    cost: s(i.cost),
    reorderLevel: s(i.reorderLevel),
  };
}

function toRequest(values: ItemForm): SaveItemRequest {
  // Blank means "not set" → null; anything else is a validated number by the time we are here.
  const num = (v: string) => (v.trim() === "" ? null : Number(v));

  return {
    name: values.name.trim(),
    unit: values.unit.trim() === "" ? null : values.unit.trim(),
    sellingPrice: num(values.sellingPrice),
    cost: num(values.cost),
    reorderLevel: num(values.reorderLevel),
  };
}

function codeOrder(code: string): number {
  const digits = code.replace(/\D/g, "");
  return digits === "" ? Number.MAX_SAFE_INTEGER : Number(digits);
}

function formatMoney(value: number): string {
  return value.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}

/** Quantities print whole when whole (12), with decimals only when they carry them (12.5). */
function formatQuantity(value: number): string {
  return value.toLocaleString(undefined, { maximumFractionDigits: 4 });
}

// Via the shared helper: the API sends UTC without a Z, so `new Date(iso)` read it as local time and
// showed a clock out by the whole offset.
const formatDate = (iso: string): string => formatInstant(iso);

function message(error: unknown) {
  return error instanceof ApiError ? error.message : "That did not work.";
}
