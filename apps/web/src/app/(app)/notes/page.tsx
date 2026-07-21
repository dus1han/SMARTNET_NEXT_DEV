"use client";

/**
 * NOTES — the personal notepad.
 *
 * Replaces the legacy Notes screen, which was a single shared textarea: it loaded the newest row of
 * `notes` and INSERTed a whole new row on every save. Forty-nine rows accumulated that way, each a full
 * snapshot of the same growing list — no titles, no list, no editing, no history, and visible to
 * everyone who held the permission.
 *
 * This keeps the one thing that screen got right (somewhere to jot things down) and fixes the rest:
 * a note has a title, notes are listed, they can be edited in place, each one carries its own audit
 * history, and they are private to the person who wrote them.
 */

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { History as HistoryIcon, MoreHorizontal, Plus, SquarePen, Trash2 } from "lucide-react";
import * as Menu from "@radix-ui/react-dropdown-menu";
import { useState } from "react";
import { z } from "zod";
import { ApiError } from "@/lib/api";
import { instantFromApi } from "@/lib/time";
import { createNote, deleteNote, listNotes, updateNote, MAX_BODY_LENGTH, MAX_TITLE_LENGTH, type NoteSummary } from "@/lib/notes";
import { PageHeader } from "@/components/shell/app-shell";
import { DataTable, type ColumnDef } from "@/components/data-table";
import { applyServerErrors, useAppForm, useReason } from "@/components/form";
import { History } from "@/components/history";
import { Button, Card, Dialog, ErrorBanner, FadeIn, Input, Textarea, toast } from "@/components/ui";

const noteSchema = z.object({
  title: z.string().min(1, "A title is required.").max(MAX_TITLE_LENGTH),
  body: z.string().min(1, "A note cannot be empty.").max(MAX_BODY_LENGTH),
});

type NoteForm = z.infer<typeof noteSchema>;

export default function NotesPage() {
  const queryClient = useQueryClient();
  const reason = useReason();

  const notes = useQuery({ queryKey: ["notes"], queryFn: listNotes });

  const [editing, setEditing] = useState<NoteSummary | "new" | null>(null);
  const [inspecting, setInspecting] = useState<NoteSummary | null>(null);

  const invalidate = () => queryClient.invalidateQueries({ queryKey: ["notes"] });

  const remove = useMutation({
    mutationFn: (v: { note: NoteSummary; reason: string }) =>
      deleteNote(v.note.id, v.note.rowVersion, v.reason),
    onSuccess: () => {
      toast.success("Note removed.");
      void invalidate();
    },
    onError: (error: unknown) => toast.error(message(error)),
  });

  const columns: ColumnDef<NoteSummary, unknown>[] = [
    {
      id: "title",
      header: "Note",
      accessorFn: (row) => row.title,
      cell: ({ row }) => {
        const note = row.original;
        return (
          <div className="min-w-0">
            <p className="truncate font-medium text-text">{note.title}</p>
            {/* One line of the body, so the list is scannable without opening each note. */}
            <p className="truncate text-xs text-muted">{firstLine(note.body)}</p>
          </div>
        );
      },
    },
    {
      id: "updated",
      header: "Last updated",
      accessorFn: (row) => row.updatedAt ?? row.createdAt,
      cell: ({ row }) => {
        const note = row.original;
        return (
          <div className="whitespace-nowrap">
            <Timestamp at={note.updatedAt ?? note.createdAt} />
            {note.updatedAt && <span className="ml-1.5 text-xs text-muted">· edited</span>}
          </div>
        );
      },
    },
    {
      id: "actions",
      header: "",
      enableSorting: false,
      meta: { align: "right" },
      cell: ({ row }) => {
        const note = row.original;
        return (
          <Menu.Root>
            <Menu.Trigger asChild>
              <Button variant="ghost" size="icon" aria-label={`Actions for ${note.title}`}>
                <MoreHorizontal />
              </Button>
            </Menu.Trigger>

            <Menu.Portal>
              <Menu.Content
                align="end"
                sideOffset={4}
                className="z-50 min-w-44 rounded-lg border border-subtle bg-surface p-1 shadow-lg data-[state=open]:animate-in data-[state=open]:fade-in-0 data-[state=open]:zoom-in-95"
              >
                <Menu.Item className={menuItem} onSelect={() => setEditing(note)}>
                  <SquarePen className="size-4 text-muted" aria-hidden />
                  Edit
                </Menu.Item>

                <Menu.Item className={menuItem} onSelect={() => setInspecting(note)}>
                  <HistoryIcon className="size-4 text-muted" aria-hidden />
                  History
                </Menu.Item>

                <Menu.Item
                  className={cnMenuDanger}
                  onSelect={() =>
                    reason.ask({
                      title: "Why is this note being removed?",
                      description: `"${note.title}" stays in the audit trail, but stops showing in your notes.`,
                      confirmLabel: "Remove note",
                      destructive: true,
                      onConfirm: (why) => remove.mutateAsync({ note, reason: why }),
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

  const error = notes.error as ApiError | null;

  return (
    <FadeIn className="space-y-6">
      <PageHeader
        title="Notes"
        description="Your own notes. Nobody else can see them."
        actions={
          <Button onClick={() => setEditing("new")}>
            <Plus className="size-4" aria-hidden />
            New note
          </Button>
        }
      />

      {error && <ErrorBanner message={error.message} correlationId={error.correlationId} />}

      <Card>
        <DataTable
          columns={columns}
          rows={notes.data ?? []}
          loading={notes.isPending}
          defaultSort={{ id: "updated", desc: true }}
          searchable={(note) => `${note.title} ${note.body}`}
          searchPlaceholder="Search your notes…"
          pageSize={15}
          empty={{
            title: "No notes yet",
            description: "Anything worth remembering goes here — it stays private to you.",
          }}
        />
      </Card>

      <NoteDialog
        key={editing === "new" ? "new" : (editing?.id ?? "closed")}
        target={editing}
        onClose={() => setEditing(null)}
        onSaved={() => void invalidate()}
      />

      <Dialog
        open={inspecting !== null}
        onOpenChange={(next) => !next && setInspecting(null)}
        size="lg"
        title={inspecting ? `History of "${inspecting.title}"` : ""}
        description="Every change to this note, and when it was made."
      >
        {inspecting && <History entityType="UserNote" entityId={inspecting.id} />}
      </Dialog>

      {reason.dialog}
    </FadeIn>
  );
}

/** The new/edit popup. One dialog for both, because the fields and the rules are identical. */
function NoteDialog({
  target,
  onClose,
  onSaved,
}: {
  target: NoteSummary | "new" | null;
  onClose: () => void;
  onSaved: () => void;
}) {
  const isNew = target === "new";
  const note = target === "new" || target === null ? null : target;

  // Keyed on the target where it is rendered, so the dialog remounts and these defaults are re-read
  // each time it opens — otherwise editing a second note would show the first one's text.
  const form = useAppForm<NoteForm>(noteSchema, {
    title: note?.title ?? "",
    body: note?.body ?? "",
  });

  const save = useMutation({
    mutationFn: (values: NoteForm) =>
      note
        ? updateNote(note.id, values.title, values.body, note.rowVersion)
        : createNote(values.title, values.body),
    onSuccess: () => {
      toast.success(isNew ? "Note added." : "Note updated.");
      onSaved();
      onClose();
    },
    onError: (error: unknown) => {
      if (error instanceof ApiError) applyServerErrors(form, error);
      toast.error(message(error));
    },
  });

  return (
    <Dialog
      open={target !== null}
      onOpenChange={(next) => !next && onClose()}
      size="lg"
      title={isNew ? "New note" : "Edit note"}
      description={isNew ? "A title and the note itself." : undefined}
    >
      <form className="space-y-4" onSubmit={form.handleSubmit((values) => save.mutateAsync(values))}>
        <Input
          label="Title"
          required
          autoFocus
          {...form.register("title")}
          error={form.formState.errors.title?.message}
        />

        <Textarea
          label="Note"
          required
          rows={12}
          {...form.register("body")}
          error={form.formState.errors.body?.message}
        />

        <div className="flex justify-end gap-2">
          <Button type="button" variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" disabled={save.isPending}>
            {save.isPending ? "Saving…" : isNew ? "Add note" : "Save changes"}
          </Button>
        </div>
      </form>
    </Dialog>
  );
}

function Timestamp({ at }: { at: string }) {
  // Shared, because the same instant used to render differently depending on which screen you were on.
  const instant = instantFromApi(at) ?? new Date(0);

  return (
    <time dateTime={instant.toISOString()} title={instant.toUTCString()} className="text-sm text-text">
      {instant.toLocaleString()}
    </time>
  );
}

const firstLine = (body: string) => body.split("\n").find((line) => line.trim()) ?? "";

const message = (error: unknown) =>
  error instanceof ApiError ? error.message : "Something went wrong. Try again.";

const menuItem =
  "flex cursor-pointer items-center gap-2 rounded-md px-2.5 py-2 text-sm outline-none data-[highlighted]:bg-surface-sunken";

const cnMenuDanger = `${menuItem} text-danger`;
