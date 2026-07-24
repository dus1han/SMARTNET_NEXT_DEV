"use client";

/**
 * Administration → Mail accounts.
 *
 * The mailboxes — add (type the name, the @domain is fixed), edit (name + password only), disable, remove,
 * and send a test. The shared server and cPanel connection live on the Dev-Admin cPanel screen. When cPanel
 * is configured there, add / edit / remove here are pushed to the real mailbox on the host.
 */

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { History as HistoryIcon, Mail, MailPlus, MoreHorizontal, Power, PowerOff, Send, Trash2 } from "lucide-react";
import * as Menu from "@radix-ui/react-dropdown-menu";
import { useState } from "react";
import type { MailAccount } from "@smartnet/api-client";
import { ApiError } from "@/lib/api";
import {
  createMailAccount,
  deleteMailAccount,
  getMailDomain,
  listMailAccounts,
  testMailAccount,
  updateMailAccount,
} from "@/lib/mail-accounts";
import { MINIMUM_REASON_LENGTH } from "@/lib/admin";
import { cn } from "@/lib/cn";
import { PageHeader } from "@/components/shell/app-shell";
import { DataTable, type ColumnDef } from "@/components/data-table";
import { useReason } from "@/components/form";
import { History } from "@/components/history";
import { Badge, Button, Dialog, ErrorBanner, FadeIn, Input, toast } from "@/components/ui";

function message(error: unknown) {
  return error instanceof ApiError ? error.message : "That did not work.";
}

export default function MailAccountsPage() {
  const queryClient = useQueryClient();
  const reason = useReason();

  const accounts = useQuery({ queryKey: ["mail-accounts"], queryFn: listMailAccounts });
  const domain = useQuery({ queryKey: ["mail-domain"], queryFn: getMailDomain });
  const invalidate = () => queryClient.invalidateQueries({ queryKey: ["mail-accounts"] });

  const [editing, setEditing] = useState<MailAccount | null>(null);
  const [creating, setCreating] = useState(false);
  const [inspecting, setInspecting] = useState<MailAccount | null>(null);
  const [testing, setTesting] = useState<MailAccount | null>(null);

  const remove = useMutation({
    mutationFn: (v: { id: number; reason: string }) => deleteMailAccount(v.id, v.reason),
    onSuccess: () => {
      toast.success("Mail account removed.");
      void invalidate();
    },
    onError: (error: unknown) => toast.error(message(error)),
  });

  const toggle = useMutation({
    mutationFn: (v: { account: MailAccount; enabled: boolean; reason: string }) =>
      updateMailAccount(
        v.account.id,
        { displayName: v.account.displayName, emailAddress: v.account.emailAddress, password: null, enabled: v.enabled },
        v.reason,
      ),
    onSuccess: (_r, v) => {
      toast.success(v.enabled ? "Mail account enabled." : "Mail account disabled.");
      void invalidate();
    },
    onError: (error: unknown) => toast.error(message(error)),
  });

  const columns: ColumnDef<MailAccount, unknown>[] = [
    {
      id: "account",
      header: "Account",
      accessorFn: (row) => row.displayName,
      cell: ({ row }) => {
        const a = row.original;
        return (
          <div className="flex items-center gap-3">
            <span className="grid size-8 shrink-0 place-items-center rounded-full bg-primary-ghost text-primary">
              <Mail className="size-4" aria-hidden />
            </span>
            <div className="min-w-0">
              <p className="truncate font-medium text-text">{a.displayName}</p>
              <p className="truncate text-xs text-muted">{a.emailAddress}</p>
            </div>
          </div>
        );
      },
    },
    {
      id: "status",
      header: "Status",
      accessorFn: (row) => (row.enabled ? "Enabled" : "Disabled"),
      cell: ({ row }) =>
        row.original.enabled ? <Badge tone="success">Enabled</Badge> : <Badge tone="danger">Disabled</Badge>,
    },
    {
      id: "actions",
      header: "",
      enableSorting: false,
      cell: ({ row }) => {
        const a = row.original;
        return (
          <Menu.Root>
            <Menu.Trigger asChild>
              <Button variant="ghost" size="icon" aria-label={`Actions for ${a.displayName}`}>
                <MoreHorizontal />
              </Button>
            </Menu.Trigger>
            <Menu.Portal>
              <Menu.Content
                align="end"
                sideOffset={4}
                className="z-50 min-w-48 rounded-lg border border-subtle bg-surface p-1 shadow-lg data-[state=open]:animate-in data-[state=open]:fade-in-0 data-[state=open]:zoom-in-95"
              >
                <Menu.Item className={menuItem} onSelect={() => setEditing(a)}>
                  <Mail className="size-4 text-muted" aria-hidden />
                  Edit
                </Menu.Item>

                <Menu.Item className={menuItem} onSelect={() => setTesting(a)}>
                  <Send className="size-4 text-muted" aria-hidden />
                  Send test
                </Menu.Item>

                <Menu.Item className={menuItem} onSelect={() => setInspecting(a)}>
                  <HistoryIcon className="size-4 text-muted" aria-hidden />
                  History
                </Menu.Item>

                <Menu.Separator className="my-1 h-px bg-subtle" />

                {a.enabled ? (
                  <Menu.Item
                    className={menuItem}
                    onSelect={() =>
                      reason.ask({
                        title: `Disable ${a.displayName}`,
                        description: "The account is marked unusable here. The real mailbox is left alone.",
                        confirmLabel: "Disable account",
                        destructive: true,
                        onConfirm: (why) => toggle.mutateAsync({ account: a, enabled: false, reason: why }),
                      })
                    }
                  >
                    <PowerOff className="size-4 text-muted" aria-hidden />
                    Disable
                  </Menu.Item>
                ) : (
                  <Menu.Item
                    className={menuItem}
                    onSelect={() =>
                      reason.ask({
                        title: `Enable ${a.displayName}`,
                        description: "The account becomes usable again.",
                        confirmLabel: "Enable account",
                        onConfirm: (why) => toggle.mutateAsync({ account: a, enabled: true, reason: why }),
                      })
                    }
                  >
                    <Power className="size-4 text-muted" aria-hidden />
                    Enable
                  </Menu.Item>
                )}

                <Menu.Item
                  className={cn(menuItem, "text-danger")}
                  onSelect={() =>
                    reason.ask({
                      title: `Remove ${a.displayName}`,
                      description:
                        "With cPanel configured this permanently DELETES the mailbox and all its email on the "
                        + "server. This cannot be undone.",
                      confirmLabel: "Remove account",
                      destructive: true,
                      onConfirm: (why) => remove.mutateAsync({ id: a.id, reason: why }),
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

  const loadError = accounts.error as ApiError | null;

  return (
    <FadeIn className="space-y-6">
      <PageHeader
        title="Mail accounts"
        description="The mailboxes on the shared server. Changes are recorded against your name."
        actions={
          <Button onClick={() => setCreating(true)}>
            <MailPlus />
            Add account
          </Button>
        }
      />

      {loadError && <ErrorBanner message={loadError.message} correlationId={loadError.correlationId} />}

      <DataTable
        columns={columns}
        rows={accounts.data}
        loading={accounts.isPending}
        searchable={(a) => `${a.displayName} ${a.emailAddress}`}
        searchPlaceholder="Search mail accounts…"
        empty={{ title: "No mail accounts yet", description: "Add one to get started." }}
      />

      <MailAccountDialog
        key={creating ? "new" : (editing?.id ?? "closed")}
        account={editing}
        domain={domain.data?.domain ?? null}
        open={creating || editing !== null}
        onClose={() => {
          setCreating(false);
          setEditing(null);
        }}
        onSaved={invalidate}
      />

      <TestDialog account={testing} onClose={() => setTesting(null)} />

      <Dialog
        open={inspecting !== null}
        onOpenChange={(next) => !next && setInspecting(null)}
        size="lg"
        title={inspecting ? `History of ${inspecting.displayName}` : ""}
        description="Every change to this account, and who made it."
      >
        {inspecting && <History entityType="MailAccount" entityId={inspecting.id} />}
      </Dialog>

      {reason.dialog}
    </FadeIn>
  );
}

const menuItem = cn(
  "flex cursor-pointer items-center gap-2 rounded-md px-2.5 py-2 text-sm outline-none",
  "transition-colors duration-150 data-[highlighted]:bg-surface-sunken",
);

/**
 * Create when `account` is null, edit otherwise.
 *  - Create: display name, the mailbox name (with the fixed @domain suffix), and a password.
 *  - Edit: display name and password only. The address is fixed — it is the mailbox on the host.
 */
function MailAccountDialog({ account, domain, open, onClose, onSaved }: {
  account: MailAccount | null;
  domain: string | null;
  open: boolean;
  onClose: () => void;
  onSaved: () => void;
}) {
  const [displayName, setDisplayName] = useState(account?.displayName ?? "");
  const [localPart, setLocalPart] = useState("");
  const [password, setPassword] = useState("");
  const [reason, setReason] = useState("");
  const [banner, setBanner] = useState<string | null>(null);

  // On create the address is <name>@<domain>; on edit it is fixed to what the account already is.
  const emailAddress = account
    ? account.emailAddress
    : domain
      ? `${localPart.trim()}@${domain}`
      : localPart.trim();

  const save = useMutation({
    mutationFn: async () => {
      if (account) {
        await updateMailAccount(
          account.id,
          { displayName: displayName.trim(), emailAddress: account.emailAddress, password: password || null, enabled: account.enabled },
          reason,
        );
      } else {
        await createMailAccount(
          { displayName: displayName.trim(), emailAddress, password: password || null, enabled: true },
          reason,
        );
      }
    },
    onSuccess: () => {
      toast.success(account ? "Mail account saved." : "Mail account added.");
      onSaved();
      onClose();
    },
    onError: (error: unknown) => setBanner(message(error)),
  });

  const nameOk = account ? true : localPart.trim() !== "";
  // The server's rule (RequireChangeReason, 10 chars). The old local check was 3, so a short reason
  // passed here and then came back a 400 the user read as "reason required" with a reason on screen.
  const canSave = displayName.trim() !== "" && nameOk && reason.trim().length >= MINIMUM_REASON_LENGTH;

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => !next && onClose()}
      title={account ? `Edit ${account.displayName}` : "Add a mail account"}
      description={
        account
          ? "The display name and password. The address is fixed — it is the mailbox on the server. The password is write-only; leave it blank to keep the stored one."
          : "Type the mailbox name; the domain is fixed. When cPanel is configured, this creates the real mailbox on the host."
      }
      footer={
        <>
          <Button variant="ghost" onClick={onClose}>
            Cancel
          </Button>
          <Button pending={save.isPending} disabled={!canSave} onClick={() => save.mutate()}>
            {account ? "Save account" : "Add account"}
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        {banner && <ErrorBanner message={banner} />}

        <Input label="Display name" required value={displayName} onChange={(e) => setDisplayName(e.target.value)} />

        {account ? (
          <Input label="Email address" value={account.emailAddress} readOnly disabled />
        ) : (
          <div>
            <label className="mb-1.5 block text-sm font-medium text-text">
              Email address <span className="text-danger">*</span>
            </label>
            <div className="flex items-stretch overflow-hidden rounded-lg border border-subtle bg-surface focus-within:ring-2 focus-within:ring-ring/25">
              <input
                className="min-w-0 flex-1 bg-transparent px-3 py-2 text-sm text-text outline-none"
                placeholder="sales"
                autoComplete="off"
                value={localPart}
                onChange={(e) => setLocalPart(e.target.value.replace(/[@\s]/g, ""))}
              />
              <span className="flex items-center whitespace-nowrap border-l border-subtle bg-surface-sunken px-3 text-sm text-muted">
                @{domain ?? "…"}
              </span>
            </div>
            {!domain && (
              <p className="mt-1 text-xs text-warning-text">
                No mail domain is set. Set it on the cPanel screen so addresses complete themselves.
              </p>
            )}
          </div>
        )}

        <Input
          label={account?.hasPassword ? "Password (leave blank to keep)" : "Password"}
          type="password"
          placeholder={account?.hasPassword ? "••••••" : ""}
          value={password}
          onChange={(e) => setPassword(e.target.value)}
        />

        <Input
          label="Reason for this change"
          placeholder="Recorded against your name (AUDIT.md §5)."
          hint={`Recorded in the audit log. At least ${MINIMUM_REASON_LENGTH} characters.`}
          value={reason}
          onChange={(e) => setReason(e.target.value)}
        />
      </div>
    </Dialog>
  );
}

/** Sends a test message through an account — a small popup that asks only for the recipient. */
function TestDialog({ account, onClose }: { account: MailAccount | null; onClose: () => void }) {
  const [to, setTo] = useState("");

  const test = useMutation({
    mutationFn: () => testMailAccount(account!.id, to),
    onSuccess: () => {
      toast.success("Test message sent.");
      setTo("");
      onClose();
    },
    onError: (error: unknown) => toast.error(message(error)),
  });

  return (
    <Dialog
      open={account !== null}
      onOpenChange={(next) => !next && onClose()}
      title={account ? `Send a test from ${account.displayName}` : ""}
      description="Proves this account reaches the shared server. Enter where to send it."
      footer={
        <>
          <Button variant="ghost" onClick={onClose}>
            Cancel
          </Button>
          <Button pending={test.isPending} disabled={!to.includes("@")} onClick={() => test.mutate()}>
            Send test
          </Button>
        </>
      }
    >
      <Input
        label="Recipient"
        type="email"
        placeholder="you@example.com"
        value={to}
        onChange={(e) => setTo(e.target.value)}
      />
    </Dialog>
  );
}
