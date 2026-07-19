"use client";

/**
 * THE REFERENCE SCREEN.
 *
 * This is the Phase 2 exit criterion: the screen every other CRUD screen is cloned from. Customers,
 * suppliers, items and expenses are all this file with a different schema and different columns.
 *
 * It is worth reading before writing the next one, because it demonstrates every piece:
 *
 *   1. `useQuery` for the list, `useMutation` for the changes.
 *   2. `DataTable` — sorting, search, pagination, export, empty state. A column definition, no more.
 *   3. `useAppForm` + zod — one schema validates the client AND types the payload.
 *   4. `applyServerErrors` — the server's field errors land ON the fields. The server is the
 *      authority; the client's copy of the rules is a convenience.
 *   5. `useReason` — the X-Change-Reason prompt, for the changes AUDIT.md makes mandatory.
 *   6. `History` — the audit trail of one record, which every module gets for free.
 *
 * The measure of Phase 2 is not how this looks. It is how little code the second screen needs.
 */

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  Copy,
  History as HistoryIcon,
  KeyRound,
  MoreHorizontal,
  ShieldCheck,
  ShieldOff,
  UserPlus,
} from "lucide-react";
import * as Menu from "@radix-ui/react-dropdown-menu";
import { useState } from "react";
import { z } from "zod";
import { ApiError } from "@/lib/api";
import {
  createUser,
  disableUser,
  listPermissions,
  listUsers,
  resetPassword,
  setUserPermissions,
  type UserSummary,
} from "@/lib/admin";
import { groupPermissions } from "@/lib/permissions";
import { cn } from "@/lib/cn";
import { PageHeader } from "@/components/shell/app-shell";
import { DataTable, type ColumnDef } from "@/components/data-table";
import { applyServerErrors, useAppForm, useReason } from "@/components/form";
import { History } from "@/components/history";
import {
  Badge,
  Button,
  Card,
  Dialog,
  ErrorBanner,
  FadeIn,
  Input,
  toast,
} from "@/components/ui";

/** The one schema. It validates the form and it types the payload — see DEVELOPMENT.md §1. */
const newUser = z.object({
  username: z
    .string()
    .min(1, "A username is required.")
    // Mirrors the server's rule. It is not the enforcement — the server re-checks it — but it is
    // the difference between a helpful message and a 400.
    .regex(/^[a-zA-Z0-9._-]+$/, "Letters, numbers, dot, underscore and hyphen only."),
  name: z.string().min(1, "A full name is required."),
});

type NewUser = z.infer<typeof newUser>;

export default function UsersPage() {
  const queryClient = useQueryClient();
  const reason = useReason();

  const users = useQuery({ queryKey: ["users"], queryFn: listUsers });

  // The permission catalogue, grouped for display. This is what the per-user editor is built from —
  // access is assigned permission by permission, not by role.
  const permissions = useQuery({ queryKey: ["permissions"], queryFn: listPermissions });
  const groups = permissions.data ? groupPermissions(permissions.data) : [];

  const [creating, setCreating] = useState(false);
  const [editing, setEditing] = useState<UserSummary | null>(null);
  const [inspecting, setInspecting] = useState<UserSummary | null>(null);

  /** Shown exactly once: the server keeps only an Argon2id hash and cannot show it again. */
  const [issued, setIssued] = useState<{ username: string; password: string } | null>(null);

  const invalidate = () => queryClient.invalidateQueries({ queryKey: ["users"] });

  const columns: ColumnDef<UserSummary, unknown>[] = [
    {
      id: "user",
      header: "User",
      accessorFn: (row) => row.name,
      cell: ({ row }) => {
        const user = row.original;

        return (
          <div className="flex items-center gap-3">
            <span className="grid size-8 shrink-0 place-items-center rounded-full bg-primary-ghost text-xs font-semibold text-primary">
              {user.username.slice(0, 2).toUpperCase()}
            </span>

            <div className="min-w-0">
              <p className="truncate font-medium text-text">{user.name}</p>
              <p className="truncate text-xs text-muted">{user.username}</p>
            </div>
          </div>
        );
      },
    },
    {
      id: "permissions",
      header: "Permissions",
      accessorFn: (row) => row.effectivePermissions.length,
      cell: ({ row }) => {
        const count = row.original.effectivePermissions.length;
        return (
          <span className="text-sm text-muted">
            {count === 0 ? (
              <span className="text-warning-text">None</span>
            ) : (
              <>
                <span className="tabular font-medium text-text">{count}</span> granted
              </>
            )}
          </span>
        );
      },
    },
    {
      id: "status",
      header: "Status",
      accessorFn: (row) => status(row).label,
      cell: ({ row }) => {
        const { label, tone } = status(row.original);
        return <Badge tone={tone}>{label}</Badge>;
      },
    },
    {
      id: "actions",
      header: "",
      enableSorting: false,
      cell: ({ row }) => {
        const user = row.original;

        return (
          <Menu.Root>
            <Menu.Trigger asChild>
              <Button variant="ghost" size="icon" aria-label={`Actions for ${user.username}`}>
                <MoreHorizontal />
              </Button>
            </Menu.Trigger>

            <Menu.Portal>
              <Menu.Content
                align="end"
                sideOffset={4}
                className="z-50 min-w-48 rounded-lg border border-subtle bg-surface p-1 shadow-lg data-[state=open]:animate-in data-[state=open]:fade-in-0 data-[state=open]:zoom-in-95"
              >
                <Menu.Item className={menuItem} onSelect={() => setEditing(user)}>
                  <ShieldCheck className="size-4 text-muted" aria-hidden />
                  Edit permissions
                </Menu.Item>

                <Menu.Item className={menuItem} onSelect={() => setInspecting(user)}>
                  <HistoryIcon className="size-4 text-muted" aria-hidden />
                  History
                </Menu.Item>

                <Menu.Item
                  className={menuItem}
                  onSelect={() =>
                    // Mandatory reason: AUDIT.md §5. The server rejects this without one.
                    reason.ask({
                      title: `Reset ${user.username}'s password`,
                      description:
                        "A single-use password is generated and shown once. They must change it at their next sign-in.",
                      confirmLabel: "Reset password",
                      // Awaited but discarded: the dialog only needs to know when it may close.
                      // The password itself is handled in onSuccess, which is the one place that
                      // knows it must be shown before it is gone forever.
                      onConfirm: async (why) => {
                        await reset.mutateAsync({ user, reason: why });
                      },
                    })
                  }
                >
                  <KeyRound className="size-4 text-muted" aria-hidden />
                  Reset password
                </Menu.Item>

                <Menu.Separator className="my-1 h-px bg-subtle" />

                <Menu.Item
                  disabled={user.isDisabled}
                  className={cn(menuItem, "text-danger data-[disabled]:opacity-40")}
                  onSelect={() =>
                    reason.ask({
                      title: `Disable ${user.username}`,
                      description:
                        "They can no longer sign in. Nothing is deleted — their history stays attributable.",
                      confirmLabel: "Disable user",
                      destructive: true,
                      onConfirm: (why) => disable.mutateAsync({ id: user.id, reason: why }),
                    })
                  }
                >
                  <ShieldOff className="size-4" aria-hidden />
                  Disable
                </Menu.Item>
              </Menu.Content>
            </Menu.Portal>
          </Menu.Root>
        );
      },
    },
  ];

  const reset = useMutation({
    mutationFn: (v: { user: UserSummary; reason: string }) => resetPassword(v.user.id, v.reason),
    onSuccess: (result, v) => {
      setIssued({ username: v.user.username, password: result.temporaryPassword });
      void invalidate();
    },
    onError: (error: unknown) => toast.error(message(error)),
  });

  const disable = useMutation({
    mutationFn: (v: { id: number; reason: string }) => disableUser(v.id, v.reason),
    onSuccess: () => {
      toast.success("User disabled.");
      void invalidate();
    },
    onError: (error: unknown) => toast.error(message(error)),
  });

  // A 403 here is not a bug: it is the server correctly refusing somebody without the `users`
  // permission who reached this page by typing its URL.
  const loadError = (users.error ?? permissions.error) as ApiError | null;

  return (
    <FadeIn className="space-y-6">
      <PageHeader
        title="Users"
        description="Changing what someone can do requires a reason. It is recorded against your name."
        actions={
          <Button onClick={() => setCreating(true)}>
            <UserPlus />
            Add user
          </Button>
        }
      />

      {loadError && (
        <ErrorBanner message={loadError.message} correlationId={loadError.correlationId} />
      )}

      {issued && (
        <TemporaryPassword
          username={issued.username}
          password={issued.password}
          onDismiss={() => setIssued(null)}
        />
      )}

      <DataTable
        columns={columns}
        rows={users.data}
        loading={users.isPending}
        searchable={(user) => `${user.name} ${user.username}`}
        searchPlaceholder="Search users…"
        exportUrl="/api/users/export"
        exportFilename="users.xlsx"
        empty={{ title: "No users yet", description: "Add one to get started." }}
      />

      <CreateUserDialog
        open={creating}
        onClose={() => setCreating(false)}
        onCreated={(username, password) => {
          setIssued({ username, password });
          void invalidate();
        }}
      />

      <EditPermissionsDialog
        user={editing}
        groups={groups}
        onClose={() => setEditing(null)}
        onSaved={invalidate}
        ask={reason.ask}
      />

      {/* 6. The History tab. The same component every document module gets in Phase 5 — here it
             reads the audit log, because until Phase 5 nothing writes document versions. A user is
             not a document, and it works anyway: that is the point of it being generic. */}
      <Dialog
        open={inspecting !== null}
        onOpenChange={(next) => !next && setInspecting(null)}
        size="lg"
        title={inspecting ? `History of ${inspecting.username}` : ""}
        description="Every change to this account, and who made it. Nothing on this screen can be edited or deleted — the application's database user holds INSERT and SELECT on the audit log and nothing else."
      >
        {inspecting && <History entityType="User" entityId={inspecting.id} />}
      </Dialog>

      {reason.dialog}
    </FadeIn>
  );
}

const menuItem = cn(
  "flex cursor-pointer items-center gap-2 rounded-md px-2.5 py-2 text-sm outline-none",
  "transition-colors duration-150 data-[highlighted]:bg-surface-sunken",
);

function status(user: UserSummary): { label: string; tone: "danger" | "warning" | "success" } {
  if (user.isDisabled) return { label: "Disabled", tone: "danger" };
  if (user.isLockedOut) return { label: "Locked out", tone: "warning" };
  if (user.mustChangePassword) return { label: "Must change password", tone: "warning" };
  return { label: "Active", tone: "success" };
}

function message(error: unknown) {
  return error instanceof ApiError ? error.message : "That did not work.";
}

/**
 * The create form. Note what it does NOT have: a password field.
 *
 * The legacy app gave every new user the password `1234` — the same one, for everybody, printed in
 * the source (ManageUserController.cs:173). The server generates a single-use password instead and
 * returns it exactly once.
 */
function CreateUserDialog({ open, onClose, onCreated }: {
  open: boolean;
  onClose: () => void;
  onCreated: (username: string, password: string) => void;
}) {
  const form = useAppForm<NewUser>(newUser, { username: "", name: "" });
  const [banner, setBanner] = useState<string[]>([]);

  const create = useMutation({
    // Created with no permissions. The account exists but can do nothing until an administrator
    // grants it access — deliberately, so nobody is created powerful by default. "Edit permissions"
    // on the new row is the next step.
    mutationFn: (values: NewUser) => createUser(values.username, values.name, []),
    onSuccess: (result, values) => {
      onCreated(values.username, result.temporaryPassword);
      form.reset();
      setBanner([]);
      onClose();
    },
    // The server's field errors land on the fields. Anything that has no field — a header, an
    // unknown key — comes back here and goes in the banner, because a rejection nobody can see is a
    // form that appears to do nothing when you press Save.
    onError: (error: unknown) => setBanner(applyServerErrors(form, error)),
  });

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => !next && onClose()}
      title="Add a user"
      description="The server generates a single-use password. There is no default password."
      footer={
        <>
          <Button variant="ghost" onClick={onClose}>
            Cancel
          </Button>

          <Button
            pending={create.isPending}
            onClick={form.handleSubmit((values) => create.mutate(values))}
          >
            Create user
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        {banner.map((text) => (
          <ErrorBanner key={text} message={text} />
        ))}

        <Input
          label="Username"
          required
          error={form.formState.errors.username?.message}
          {...form.register("username")}
        />

        <Input
          label="Full name"
          required
          error={form.formState.errors.name?.message}
          {...form.register("name")}
        />

        <p className="rounded-lg border border-subtle bg-surface-sunken px-3 py-2.5 text-xs text-muted">
          The account starts with no permissions. Once it is created, use{" "}
          <span className="font-medium text-text">Edit permissions</span> to grant access.
        </p>
      </div>
    </Dialog>
  );
}

/**
 * Assigns a user's permissions directly — the whole set, ticked one by one.
 *
 * This replaces role assignment for ordinary users. What someone may do is a list of permissions,
 * grouped the way the app is (Sales, Purchasing, Reports, …), and the boxes are the truth: on save,
 * the server makes the user's effective permissions equal exactly what is ticked. Roles still exist
 * underneath for the two system administrators, but nobody managing a normal user has to think about
 * them.
 */
function EditPermissionsDialog({ user, groups, onClose, onSaved, ask }: {
  user: UserSummary | null;
  groups: import("@/lib/permissions").PermissionGroup[];
  onClose: () => void;
  onSaved: () => void;
  ask: (request: import("@/components/form").ReasonRequest) => void;
}) {
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [loaded, setLoaded] = useState<number | null>(null);

  // Sync the ticks to whichever user was opened — their current EFFECTIVE permissions — during
  // render rather than in an effect.
  if (user && loaded !== user.id) {
    setSelected(new Set(user.effectivePermissions));
    setLoaded(user.id);
  }

  const toggle = (key: string, on: boolean) =>
    setSelected((current) => {
      const next = new Set(current);
      if (on) next.add(key);
      else next.delete(key);
      return next;
    });

  const setGroup = (keys: string[], on: boolean) =>
    setSelected((current) => {
      const next = new Set(current);
      for (const key of keys) {
        if (on) next.add(key);
        else next.delete(key);
      }
      return next;
    });

  // An exclusive group holds one of its keys and never two. Picking one clears its siblings here
  // rather than leaving the server to reject the combination after the administrator has saved.
  const choose = (keys: string[], key: string) =>
    setSelected((current) => {
      const next = new Set(current);
      for (const sibling of keys) next.delete(sibling);
      next.add(key);
      return next;
    });

  const save = useMutation({
    mutationFn: (v: { reason: string }) =>
      setUserPermissions(user!.id, [...selected], v.reason),
    onSuccess: () => {
      toast.success("Permissions saved.");
      onSaved();
      onClose();
    },
    onError: (error: unknown) => toast.error(message(error)),
  });

  const total = groups.reduce((sum, g) => sum + g.items.length, 0);

  // A new user starts with no dashboard, so the radios open with nothing chosen and there is a real
  // "not yet valid" state to hold the save against. Named rather than counted so the message can say
  // which section still needs an answer.
  const unresolved = groups.filter(
    (g) => g.exclusive && g.items.filter((i) => selected.has(i.key)).length !== 1,
  );

  return (
    <Dialog
      open={user !== null}
      onOpenChange={(next) => !next && onClose()}
      size="lg"
      title={user ? `Permissions for ${user.username}` : ""}
      description="Tick what this person may do. The change is recorded against your name."
      footer={
        <>
          <Button variant="ghost" onClick={onClose}>
            Cancel
          </Button>

          <Button
            pending={save.isPending}
            disabled={unresolved.length > 0}
            title={unresolved.length > 0 ? `Choose one option under ${unresolved[0].title}.` : undefined}
            onClick={() =>
              // Changing what someone may do is one of the actions AUDIT.md §5 makes mandatory. The
              // server rejects this call without an X-Change-Reason header.
              ask({
                title: "Why is this changing?",
                description: `${user?.username}'s permissions are about to change.`,
                confirmLabel: "Save permissions",
                onConfirm: (why) => save.mutateAsync({ reason: why }),
              })
            }
          >
            Save permissions
          </Button>
        </>
      }
    >
      <div className="max-h-[55vh] space-y-5 overflow-y-auto pr-1">
        <p className="text-xs text-muted">
          {selected.size} of {total} granted.
        </p>

        {unresolved.map((g) => (
          <p key={g.title} className="text-xs text-warning-text">
            Choose one option under {g.title} before saving.
          </p>
        ))}

        {groups.map((group) => {
          const keys = group.items.map((i) => i.key);
          const allOn = keys.every((k) => selected.has(k));

          return (
            <fieldset key={group.title}>
              <div className="mb-2 flex items-center justify-between gap-3 border-b border-subtle pb-1.5">
                <legend className="text-xs font-semibold uppercase tracking-wide text-muted">
                  {group.title}
                </legend>
                {/* Select-all would produce exactly the two states an exclusive group forbids. */}
                {group.exclusive ? (
                  <span className="text-xs text-muted">Choose one</span>
                ) : (
                  <button
                    type="button"
                    onClick={() => setGroup(keys, !allOn)}
                    className="text-xs font-medium text-primary hover:underline"
                  >
                    {allOn ? "Clear all" : "Select all"}
                  </button>
                )}
              </div>

              <div className="grid gap-x-4 gap-y-1 sm:grid-cols-2">
                {group.items.map((item) => {
                  const on = selected.has(item.key);

                  return (
                    <label
                      key={item.key}
                      className="flex cursor-pointer items-start gap-2.5 rounded-md px-1.5 py-1.5 hover:bg-surface-sunken"
                      title={item.hint}
                    >
                      <input
                        type={group.exclusive ? "radio" : "checkbox"}
                        // A shared name is what makes arrow keys move between the options rather
                        // than in and out of a lone radio.
                        name={group.exclusive ? `perm-${group.title}` : undefined}
                        className={cn(
                          "mt-0.5 size-4 border-subtle text-primary focus-visible:ring-2 focus-visible:ring-ring/25",
                          group.exclusive ? "rounded-full" : "rounded",
                        )}
                        checked={on}
                        onChange={(e) =>
                          group.exclusive ? choose(keys, item.key) : toggle(item.key, e.target.checked)
                        }
                      />
                      <span className="min-w-0">
                        <span className="block text-sm text-text">{item.label}</span>
                        {item.hint && <span className="block text-xs text-muted">{item.hint}</span>}
                      </span>
                    </label>
                  );
                })}
              </div>
            </fieldset>
          );
        })}
      </div>
    </Dialog>
  );
}

function TemporaryPassword({ username, password, onDismiss }: {
  username: string;
  password: string;
  onDismiss: () => void;
}) {
  return (
    <Card className="animate-in fade-in-0 zoom-in-95 border-warning/40 bg-warning-subtle duration-300">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div className="min-w-0">
          <p className="text-sm font-medium text-warning-text">Temporary password for {username}</p>

          <p className="mt-2 select-all font-mono text-lg text-text">{password}</p>

          <p className="mt-2 max-w-prose text-sm text-warning-text/80">
            Copy it now — it is stored only as a hash and cannot be shown again. They must change it
            the first time they sign in.
          </p>
        </div>

        <div className="flex gap-2">
          <Button
            variant="secondary"
            size="sm"
            onClick={() => {
              void navigator.clipboard.writeText(password);
              toast.success("Copied to the clipboard.");
            }}
          >
            <Copy />
            Copy
          </Button>

          <Button variant="ghost" size="sm" onClick={onDismiss}>
            Dismiss
          </Button>
        </div>
      </div>
    </Card>
  );
}
