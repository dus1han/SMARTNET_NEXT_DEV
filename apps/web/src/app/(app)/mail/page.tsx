"use client";

/**
 * Mail — the signed-in user's own mailboxes, worked from inside the app.
 *
 * Three panes: the mailboxes assigned to this user (with unread badges), the selected mailbox's inbox, and
 * the open message. Reading is over IMAP; sending — compose and reply — is over SMTP as that mailbox. The
 * server scopes everything to the caller's assigned mailboxes, so this screen never names a user id.
 *
 * This is the read + send core. Folders, attachments on the way out, delete, search and flags come later.
 */

import { useEffect, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { AlertTriangle, Inbox, Mail, Paperclip, PenSquare, RefreshCw, Reply, Send } from "lucide-react";
import type { MailHeader, MailboxListItem, MailMessage } from "@smartnet/api-client";
import { ApiError } from "@/lib/api";
import { listInbox, listMyMailboxes, readMessage, sendMail } from "@/lib/mail";
import { cn } from "@/lib/cn";
import { PageHeader } from "@/components/shell/app-shell";
import { Button, Dialog, ErrorBanner, FadeIn, Input, Skeleton, Textarea, toast } from "@/components/ui";

function message(error: unknown) {
  return error instanceof ApiError ? error.message : "That did not work.";
}

function when(iso: string) {
  const date = new Date(iso);
  const today = new Date();
  const sameDay = date.toDateString() === today.toDateString();
  return sameDay
    ? date.toLocaleTimeString(undefined, { hour: "2-digit", minute: "2-digit" })
    : date.toLocaleDateString(undefined, { day: "2-digit", month: "short", year: "numeric" });
}

interface Compose {
  to: string;
  subject: string;
  body: string;
}

export default function MailPage() {
  const queryClient = useQueryClient();

  const mailboxes = useQuery({ queryKey: ["my-mailboxes"], queryFn: listMyMailboxes });

  const [picked, setPicked] = useState<number | null>(null);
  const [uid, setUid] = useState<number | null>(null);
  const [compose, setCompose] = useState<Compose | null>(null);

  // The selected mailbox, defaulting to the first once the list arrives — derived during render rather
  // than set from an effect, so landing on it costs no extra render.
  const mailboxId = picked ?? mailboxes.data?.[0]?.id ?? null;

  const inbox = useQuery({
    queryKey: ["inbox", mailboxId],
    queryFn: () => listInbox(mailboxId!),
    enabled: mailboxId !== null,
  });

  const open = useQuery({
    queryKey: ["message", mailboxId, uid],
    queryFn: () => readMessage(mailboxId!, uid!),
    enabled: mailboxId !== null && uid !== null,
    // A message does not change once read; opening it already marked it seen, so never refetch it.
    staleTime: Infinity,
  });

  // Opening a message marks it read on the server — reflect that in the unread badge and the inbox row.
  useEffect(() => {
    if (open.data) {
      void queryClient.invalidateQueries({ queryKey: ["my-mailboxes"] });
      void queryClient.invalidateQueries({ queryKey: ["inbox", mailboxId] });
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open.data?.uid]);

  const selectMailbox = (id: number) => {
    setPicked(id);
    setUid(null);
  };

  const send = useMutation({
    mutationFn: (draft: Compose) => sendMail(mailboxId!, draft),
    onSuccess: () => {
      toast.success("Message sent.");
      setCompose(null);
    },
    onError: (error: unknown) => toast.error(message(error)),
  });

  const startReply = (msg: MailMessage) =>
    setCompose({
      to: msg.fromAddress,
      subject: msg.subject.toLowerCase().startsWith("re:") ? msg.subject : `Re: ${msg.subject}`,
      body: `\n\n----- On ${when(msg.date)}, ${msg.fromName || msg.fromAddress} wrote -----\n`,
    });

  if (mailboxes.error) {
    const e = mailboxes.error as ApiError;
    return (
      <FadeIn className="space-y-6">
        <PageHeader title="Mail" description="Your mailboxes." />
        <ErrorBanner message={e.message} correlationId={e.correlationId} />
      </FadeIn>
    );
  }

  const noMailboxes = mailboxes.data && mailboxes.data.length === 0;

  return (
    <FadeIn className="space-y-6">
      <PageHeader
        title="Mail"
        description="The mailboxes assigned to you. Read, reply and compose."
        actions={
          <Button onClick={() => setCompose({ to: "", subject: "", body: "" })} disabled={mailboxId === null}>
            <PenSquare />
            Compose
          </Button>
        }
      />

      {noMailboxes ? (
        <div className="rounded-xl border border-subtle bg-surface-sunken/40 p-10 text-center">
          <Mail className="mx-auto size-8 text-muted" aria-hidden />
          <p className="mt-3 text-sm font-medium text-text">No mailboxes are assigned to you yet.</p>
          <p className="mt-1 text-sm text-muted">An administrator assigns mailboxes on the Users screen.</p>
        </div>
      ) : (
        <div className="grid gap-4 lg:grid-cols-[15rem_20rem_1fr]">
          <MailboxSwitcher
            mailboxes={mailboxes.data}
            loading={mailboxes.isPending}
            selected={mailboxId}
            onSelect={selectMailbox}
          />

          <InboxList
            headers={inbox.data}
            loading={inbox.isPending && mailboxId !== null}
            error={inbox.error as ApiError | null}
            selected={uid}
            onSelect={setUid}
            onRefresh={() => inbox.refetch()}
          />

          <ReadingPane
            query={open}
            hasSelection={uid !== null}
            onReply={startReply}
          />
        </div>
      )}

      <ComposeDialog
        draft={compose}
        from={mailboxes.data?.find((m) => m.id === mailboxId)?.emailAddress ?? ""}
        pending={send.isPending}
        onChange={setCompose}
        onSend={() => compose && send.mutate(compose)}
        onClose={() => setCompose(null)}
      />
    </FadeIn>
  );
}

function MailboxSwitcher({ mailboxes, loading, selected, onSelect }: {
  mailboxes: MailboxListItem[] | undefined;
  loading: boolean;
  selected: number | null;
  onSelect: (id: number) => void;
}) {
  return (
    <div className="space-y-1 rounded-xl border border-subtle bg-surface p-2">
      <p className="px-2 py-1.5 text-xs font-semibold uppercase tracking-wide text-muted">Mailboxes</p>

      {loading && <Skeleton className="h-20" />}

      {mailboxes?.map((box) => {
        const active = box.id === selected;
        return (
          <button
            key={box.id}
            type="button"
            onClick={() => onSelect(box.id)}
            className={cn(
              "flex w-full items-center gap-2 rounded-lg px-2.5 py-2 text-left transition-colors",
              active ? "bg-primary-ghost" : "hover:bg-surface-sunken",
            )}
          >
            <Inbox className={cn("size-4 shrink-0", active ? "text-primary" : "text-muted")} aria-hidden />
            <span className="min-w-0 flex-1">
              <span className="block truncate text-sm font-medium text-text">{box.displayName}</span>
              <span className="block truncate text-xs text-muted">{box.emailAddress}</span>
            </span>
            {box.error ? (
              <span title={box.error} className="shrink-0">
                <AlertTriangle className="size-4 text-warning-text" aria-label={box.error} />
              </span>
            ) : box.unread ? (
              <span className="shrink-0 rounded-full bg-primary px-1.5 py-0.5 text-xs font-semibold tabular-nums text-white">
                {box.unread}
              </span>
            ) : null}
          </button>
        );
      })}
    </div>
  );
}

function InboxList({ headers, loading, error, selected, onSelect, onRefresh }: {
  headers: MailHeader[] | undefined;
  loading: boolean;
  error: ApiError | null;
  selected: number | null;
  onSelect: (uid: number) => void;
  onRefresh: () => void;
}) {
  return (
    <div className="flex min-h-[60vh] flex-col rounded-xl border border-subtle bg-surface">
      <div className="flex items-center justify-between border-b border-subtle px-3 py-2">
        <span className="text-xs font-semibold uppercase tracking-wide text-muted">Inbox</span>
        <Button variant="ghost" size="icon" onClick={onRefresh} aria-label="Refresh inbox">
          <RefreshCw className="size-4" />
        </Button>
      </div>

      <div className="min-h-0 flex-1 overflow-y-auto">
        {loading && <div className="p-3"><Skeleton className="h-40" /></div>}

        {error && <div className="p-3"><ErrorBanner message={error.message} correlationId={error.correlationId} /></div>}

        {headers && headers.length === 0 && (
          <p className="p-6 text-center text-sm text-muted">This inbox is empty.</p>
        )}

        {headers?.map((h) => {
          const active = h.uid === selected;
          return (
            <button
              key={h.uid}
              type="button"
              onClick={() => onSelect(h.uid)}
              className={cn(
                "flex w-full flex-col gap-0.5 border-b border-subtle px-3 py-2.5 text-left transition-colors",
                active ? "bg-primary-ghost" : "hover:bg-surface-sunken",
              )}
            >
              <div className="flex items-center gap-2">
                {!h.seen && <span className="size-2 shrink-0 rounded-full bg-primary" aria-label="Unread" />}
                <span className={cn("min-w-0 flex-1 truncate text-sm", h.seen ? "text-text" : "font-semibold text-text")}>
                  {h.fromName || h.fromAddress}
                </span>
                {h.hasAttachments && <Paperclip className="size-3.5 shrink-0 text-muted" aria-hidden />}
                <span className="shrink-0 text-xs text-muted">{when(h.date)}</span>
              </div>
              <span className={cn("truncate text-xs", h.seen ? "text-muted" : "text-text")}>{h.subject}</span>
            </button>
          );
        })}
      </div>
    </div>
  );
}

function ReadingPane({ query, hasSelection, onReply }: {
  query: { data?: MailMessage; isPending: boolean; error: unknown };
  hasSelection: boolean;
  onReply: (msg: MailMessage) => void;
}) {
  if (!hasSelection) {
    return (
      <div className="grid min-h-[60vh] place-items-center rounded-xl border border-subtle bg-surface-sunken/30 text-center">
        <div>
          <Mail className="mx-auto size-8 text-muted" aria-hidden />
          <p className="mt-2 text-sm text-muted">Select a message to read it.</p>
        </div>
      </div>
    );
  }

  if (query.error) {
    const e = query.error as ApiError;
    return (
      <div className="min-h-[60vh] rounded-xl border border-subtle bg-surface p-4">
        <ErrorBanner message={e.message} correlationId={e.correlationId} />
      </div>
    );
  }

  if (query.isPending || !query.data) {
    return (
      <div className="min-h-[60vh] rounded-xl border border-subtle bg-surface p-4">
        <Skeleton className="h-64" />
      </div>
    );
  }

  const msg = query.data;

  return (
    <div className="flex min-h-[60vh] flex-col rounded-xl border border-subtle bg-surface">
      <div className="border-b border-subtle p-4">
        <div className="flex items-start justify-between gap-3">
          <h2 className="text-base font-semibold text-text">{msg.subject}</h2>
          <Button variant="secondary" size="sm" onClick={() => onReply(msg)}>
            <Reply className="size-4" />
            Reply
          </Button>
        </div>
        <p className="mt-2 text-sm text-text">
          <span className="font-medium">{msg.fromName || msg.fromAddress}</span>{" "}
          {msg.fromName && <span className="text-muted">&lt;{msg.fromAddress}&gt;</span>}
        </p>
        <p className="text-xs text-muted">
          To: {msg.to || "—"} · {new Date(msg.date).toLocaleString()}
        </p>
      </div>

      <div className="min-h-0 flex-1 overflow-hidden p-1">
        {msg.isHtml ? (
          // Sandboxed with no allowances — the message cannot run scripts, submit forms or reach our origin.
          <iframe
            title="Message"
            sandbox=""
            srcDoc={msg.body}
            className="h-full min-h-[50vh] w-full rounded-lg border border-subtle bg-white"
          />
        ) : (
          <pre className="h-full overflow-auto whitespace-pre-wrap p-3 font-sans text-sm text-text">{msg.body}</pre>
        )}
      </div>
    </div>
  );
}

function ComposeDialog({ draft, from, pending, onChange, onSend, onClose }: {
  draft: Compose | null;
  from: string;
  pending: boolean;
  onChange: (draft: Compose) => void;
  onSend: () => void;
  onClose: () => void;
}) {
  const canSend = !!draft && draft.to.trim() !== "";

  return (
    <Dialog
      open={draft !== null}
      onOpenChange={(next) => !next && onClose()}
      size="lg"
      title="New message"
      description={from ? `Sending as ${from}.` : undefined}
      footer={
        <>
          <Button variant="ghost" onClick={onClose}>
            Cancel
          </Button>
          <Button pending={pending} disabled={!canSend} onClick={onSend}>
            <Send className="size-4" />
            Send
          </Button>
        </>
      }
    >
      {draft && (
        <div className="space-y-4">
          <Input
            label="To"
            placeholder="name@example.com, another@example.com"
            value={draft.to}
            onChange={(e) => onChange({ ...draft, to: e.target.value })}
          />
          <Input
            label="Subject"
            value={draft.subject}
            onChange={(e) => onChange({ ...draft, subject: e.target.value })}
          />
          <Textarea
            label="Message"
            rows={12}
            value={draft.body}
            onChange={(e) => onChange({ ...draft, body: e.target.value })}
          />
        </div>
      )}
    </Dialog>
  );
}
