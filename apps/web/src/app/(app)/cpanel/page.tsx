"use client";

/**
 * Administration → cPanel (Dev-Admin only).
 *
 * The one shared mail server every account uses — outgoing (SMTP) and incoming (IMAP/POP3) — and the cPanel
 * connection that lets adding a mailbox here create the real one on the host. The API token is write-only.
 * Restricted to Dev-Admin: the token can create and re-password real mailboxes. The mailboxes themselves are
 * the Mail accounts screen.
 */

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Server, ShieldCheck } from "lucide-react";
import { useState } from "react";
import type { SaveMailServerSettingsRequest } from "@smartnet/api-client";
import { MINIMUM_REASON_LENGTH } from "@/lib/admin";
import { ApiError } from "@/lib/api";
import { getMailServerSettings, saveMailServerSettings } from "@/lib/mail-accounts";
import { PageHeader } from "@/components/shell/app-shell";
import { Button, Card, ErrorBanner, FadeIn, Input, Skeleton, toast } from "@/components/ui";

type Draft = Omit<SaveMailServerSettingsRequest, "cpanelApiToken">;

const checkbox = "size-4 rounded border-subtle text-primary focus-visible:ring-2 focus-visible:ring-ring/25";

function message(error: unknown) {
  return error instanceof ApiError ? error.message : "That did not work.";
}

export default function CpanelPage() {
  const queryClient = useQueryClient();
  const server = useQuery({ queryKey: ["mail-server-settings"], queryFn: getMailServerSettings });

  const [draft, setDraft] = useState<Draft | null>(null);
  const [token, setToken] = useState("");
  const [reason, setReason] = useState("");
  const [hasToken, setHasToken] = useState(false);

  // Seed the editable draft from the loaded settings, once.
  if (server.data && draft === null) {
    const { hasCpanelApiToken, ...rest } = server.data;
    setDraft(rest);
    setHasToken(hasCpanelApiToken);
  }

  const set = <K extends keyof Draft>(key: K, value: Draft[K]) =>
    setDraft((d) => (d ? { ...d, [key]: value } : d));

  const save = useMutation({
    mutationFn: () => saveMailServerSettings({ ...draft!, cpanelApiToken: token || null }, reason),
    onSuccess: () => {
      toast.success("cPanel settings saved.");
      setToken("");
      if (token) setHasToken(true);
      setReason("");
      // The domain feeds the Mail accounts add screen's @suffix, and these settings feed a re-open of
      // this page — refresh both so neither shows a stale copy after a save.
      void queryClient.invalidateQueries({ queryKey: ["mail-server-settings"] });
      void queryClient.invalidateQueries({ queryKey: ["mail-domain"] });
    },
    onError: (error: unknown) => toast.error(message(error)),
  });

  if (server.error) {
    const e = server.error as ApiError;
    return (
      <FadeIn className="space-y-6">
        <PageHeader title="cPanel" description="The shared mail server and cPanel connection." />
        <ErrorBanner message={e.message} correlationId={e.correlationId} />
      </FadeIn>
    );
  }

  if (!draft) {
    return (
      <FadeIn className="space-y-6">
        <PageHeader title="cPanel" description="The shared mail server and cPanel connection." />
        <Skeleton className="h-64" />
      </FadeIn>
    );
  }

  // cPanel is all-or-nothing: a host without a username (or the reverse) can never authenticate.
  const cpanelHalf = !!draft.cpanelHost?.trim() !== !!draft.cpanelUsername?.trim();
  const canSave =
    draft.outgoingHost.trim() !== ""
    && draft.incomingHost.trim() !== ""
    && !cpanelHalf
    && reason.trim().length >= MINIMUM_REASON_LENGTH;

  return (
    <FadeIn className="space-y-6">
      <PageHeader
        title="cPanel"
        description="The shared mail server every account uses, and the cPanel connection that provisions mailboxes. Dev-Admin only."
      />

      <Card>
        <div className="flex items-center gap-2">
          <Server className="size-4 text-muted" aria-hidden />
          <h2 className="text-sm font-semibold text-text">Shared mail server</h2>
        </div>
        <p className="mt-1 text-xs text-muted">
          Every account reaches this one server. Set it here; each account is then just its address and password.
        </p>

        <div className="mt-5 max-w-sm">
          <Input
            label="Mail domain"
            placeholder="smart-net.lk"
            value={draft.mailDomain ?? ""}
            onChange={(e) => set("mailDomain", e.target.value)}
          />
          <p className="mt-1 text-xs text-muted">
            The part after the @. New accounts type only the name and get the full address.
          </p>
        </div>

        <div className="mt-6 grid gap-4 lg:grid-cols-2">
          <fieldset className="rounded-xl border border-subtle bg-surface-sunken/40 p-4">
            <legend className="px-1.5 text-xs font-semibold uppercase tracking-wide text-muted">
              Outgoing (SMTP)
            </legend>
            <div className="mt-1 grid grid-cols-[1fr_6rem] gap-3">
              <Input
                label="Host"
                placeholder="mail.smart-net.lk"
                value={draft.outgoingHost}
                onChange={(e) => set("outgoingHost", e.target.value)}
              />
              <Input
                label="Port"
                type="number"
                value={draft.outgoingPort}
                onChange={(e) => set("outgoingPort", Number(e.target.value))}
              />
            </div>
            <label className="mt-4 flex items-center gap-2 border-t border-subtle pt-3 text-sm text-text">
              <input
                type="checkbox"
                className={checkbox}
                checked={draft.outgoingUseSsl}
                onChange={(e) => set("outgoingUseSsl", e.target.checked)}
              />
              Use TLS/SSL
            </label>
          </fieldset>

          <fieldset className="rounded-xl border border-subtle bg-surface-sunken/40 p-4">
            <legend className="px-1.5 text-xs font-semibold uppercase tracking-wide text-muted">
              Incoming (IMAP / POP3)
            </legend>
            <div className="mt-1 space-y-3">
              <label className="block max-w-[10rem]">
                <span className="mb-1.5 block text-sm font-medium text-text">Protocol</span>
                <select
                  className="h-10 w-full rounded-lg border border-subtle bg-surface px-3 text-sm text-text focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring/25"
                  value={draft.incomingProtocol}
                  onChange={(e) => set("incomingProtocol", e.target.value)}
                >
                  <option value="IMAP">IMAP</option>
                  <option value="POP3">POP3</option>
                </select>
              </label>
              <div className="grid grid-cols-[1fr_6rem] gap-3">
                <Input
                  label="Host"
                  placeholder="mail.smart-net.lk"
                  value={draft.incomingHost}
                  onChange={(e) => set("incomingHost", e.target.value)}
                />
                <Input
                  label="Port"
                  type="number"
                  value={draft.incomingPort}
                  onChange={(e) => set("incomingPort", Number(e.target.value))}
                />
              </div>
            </div>
            <label className="mt-4 flex items-center gap-2 border-t border-subtle pt-3 text-sm text-text">
              <input
                type="checkbox"
                className={checkbox}
                checked={draft.incomingUseSsl}
                onChange={(e) => set("incomingUseSsl", e.target.checked)}
              />
              Use TLS/SSL
            </label>
          </fieldset>
        </div>
      </Card>

      <Card>
        <div className="flex items-center gap-2">
          <ShieldCheck className="size-4 text-muted" aria-hidden />
          <h2 className="text-sm font-semibold text-text">cPanel provisioning</h2>
        </div>
        <p className="mt-1 text-xs text-muted">
          Fill in the connection below and it is active — adding a mailbox (or changing its password) then
          creates it on the host through cPanel&rsquo;s API, so it appears in cPanel and Roundcube. Leave it
          blank and accounts are stored here only. Removing an account never deletes the real mailbox.
        </p>

        <div className="mt-5 grid gap-4 sm:grid-cols-2">
          <Input
            label="cPanel host"
            placeholder="mail.smart-net.lk"
            value={draft.cpanelHost ?? ""}
            onChange={(e) => set("cpanelHost", e.target.value)}
          />
          <Input
            label="Port"
            type="number"
            value={draft.cpanelPort}
            onChange={(e) => set("cpanelPort", Number(e.target.value))}
          />
          <Input
            label="cPanel username"
            value={draft.cpanelUsername ?? ""}
            onChange={(e) => set("cpanelUsername", e.target.value)}
          />
          <Input
            label={hasToken ? "API token (leave blank to keep)" : "API token"}
            type="password"
            placeholder={hasToken ? "••••••" : ""}
            value={token}
            onChange={(e) => setToken(e.target.value)}
          />
        </div>
        <p className="mt-3 rounded-lg border border-subtle bg-surface-sunken px-3 py-2.5 text-xs text-muted">
          Create a token in cPanel → <span className="font-medium text-text">Manage API Tokens</span>. It is
          stored encrypted and never shown again here.
        </p>
      </Card>

      <div className="flex flex-wrap items-end gap-3">
        <Input
          label="Reason for this change"
          placeholder="Recorded against your name."
          hint={`Recorded in the audit log. At least ${MINIMUM_REASON_LENGTH} characters.`}
          className="min-w-64 flex-1"
          value={reason}
          onChange={(e) => setReason(e.target.value)}
        />
        <Button pending={save.isPending} disabled={!canSave} onClick={() => save.mutate()}>
          Save
        </Button>
      </div>
    </FadeIn>
  );
}
