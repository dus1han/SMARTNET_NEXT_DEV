"use client";

/**
 * Mail — the signed-in user's own mailboxes, worked from inside the app.
 *
 * Reading is over IMAP; sending — compose and reply — is over SMTP as that mailbox. Everything is scoped
 * server-side to the caller's assigned mailboxes, so this screen never names a user id.
 *
 * Layout: a compact mailbox picker (a dropdown, not a column, so the mail gets the width), an inbox list,
 * and — when a message is opened — a full-width reading view with a Back. The page scrolls like every other
 * screen; nothing here traps the scroll in a fixed-height pane.
 *
 * This is the read + send core. Folders, attachments, delete, search and flags are follow-up slices.
 */

import { useEffect, useRef, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import * as Menu from "@radix-ui/react-dropdown-menu";
import { AlertTriangle, ArrowLeft, ChevronDown, Inbox, Mail, Paperclip, PenSquare, RefreshCw, Reply, Send } from "lucide-react";
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
  const sameDay = date.toDateString() === new Date().toDateString();
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

  // Selected mailbox, defaulting to the first once the list arrives — derived, not set from an effect.
  const mailboxId = picked ?? mailboxes.data?.[0]?.id ?? null;
  const activeMailbox = mailboxes.data?.find((m) => m.id === mailboxId) ?? null;

  const inbox = useQuery({
    queryKey: ["inbox", mailboxId],
    queryFn: () => listInbox(mailboxId!),
    enabled: mailboxId !== null,
  });

  const open = useQuery({
    queryKey: ["message", mailboxId, uid],
    queryFn: () => readMessage(mailboxId!, uid!),
    enabled: mailboxId !== null && uid !== null,
    staleTime: Infinity, // a read message does not change, and opening it already marked it seen
  });

  // Opening a message marks it read on the server — reflect that in the badge and the inbox row.
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
    <FadeIn className="space-y-4">
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
        <>
          <div className="flex flex-wrap items-center gap-2">
            <MailboxPicker
              mailboxes={mailboxes.data}
              loading={mailboxes.isPending}
              active={activeMailbox}
              onSelect={selectMailbox}
            />
            {uid === null && (
              <Button variant="ghost" size="icon" onClick={() => inbox.refetch()} aria-label="Refresh inbox">
                <RefreshCw className="size-4" />
              </Button>
            )}
          </div>

          {uid === null ? (
            <InboxList
              headers={inbox.data}
              loading={inbox.isPending && mailboxId !== null}
              error={inbox.error as ApiError | null}
              onSelect={setUid}
            />
          ) : (
            <MessageView query={open} onBack={() => setUid(null)} onReply={startReply} />
          )}
        </>
      )}

      <ComposeDialog
        draft={compose}
        from={activeMailbox?.emailAddress ?? ""}
        pending={send.isPending}
        onChange={setCompose}
        onSend={() => compose && send.mutate(compose)}
        onClose={() => setCompose(null)}
      />
    </FadeIn>
  );
}

/** A dropdown, so the mailboxes cost a button's width rather than a whole column. */
function MailboxPicker({ mailboxes, loading, active, onSelect }: {
  mailboxes: MailboxListItem[] | undefined;
  loading: boolean;
  active: MailboxListItem | null;
  onSelect: (id: number) => void;
}) {
  if (loading) return <Skeleton className="h-10 w-64" />;

  return (
    <Menu.Root>
      <Menu.Trigger asChild>
        <button
          type="button"
          className="flex min-w-64 items-center gap-2 rounded-lg border border-subtle bg-surface px-3 py-2 text-left transition-colors hover:bg-surface-sunken"
        >
          <Inbox className="size-4 shrink-0 text-primary" aria-hidden />
          <span className="min-w-0 flex-1">
            <span className="block truncate text-sm font-medium text-text">{active?.displayName ?? "Select a mailbox"}</span>
            <span className="block truncate text-xs text-muted">{active?.emailAddress}</span>
          </span>
          {unreadPill(active)}
          <ChevronDown className="size-4 shrink-0 text-muted" aria-hidden />
        </button>
      </Menu.Trigger>
      <Menu.Portal>
        <Menu.Content
          align="start"
          sideOffset={4}
          className="z-50 max-h-96 w-[var(--radix-dropdown-menu-trigger-width)] overflow-y-auto rounded-lg border border-subtle bg-surface p-1.5 shadow-lg data-[state=open]:animate-in data-[state=open]:fade-in-0 data-[state=open]:zoom-in-95"
        >
          {mailboxes?.map((box) => (
            <Menu.Item
              key={box.id}
              onSelect={() => onSelect(box.id)}
              className="flex cursor-pointer items-center gap-2 rounded-md px-2.5 py-2 outline-none data-[highlighted]:bg-surface-sunken"
            >
              <Inbox className="size-4 shrink-0 text-muted" aria-hidden />
              <span className="min-w-0 flex-1">
                <span className="block truncate text-sm font-medium text-text">{box.displayName}</span>
                <span className="block truncate text-xs text-muted">{box.emailAddress}</span>
              </span>
              {unreadPill(box)}
            </Menu.Item>
          ))}
        </Menu.Content>
      </Menu.Portal>
    </Menu.Root>
  );
}

function unreadPill(box: MailboxListItem | null) {
  if (!box) return null;
  if (box.error) {
    return (
      <span title={box.error} className="shrink-0">
        <AlertTriangle className="size-4 text-warning-text" aria-label={box.error} />
      </span>
    );
  }
  if (box.unread) {
    return (
      <span className="shrink-0 rounded-full bg-primary px-1.5 py-0.5 text-xs font-semibold tabular-nums text-white">
        {box.unread}
      </span>
    );
  }
  return null;
}

function InboxList({ headers, loading, error, onSelect }: {
  headers: MailHeader[] | undefined;
  loading: boolean;
  error: ApiError | null;
  onSelect: (uid: number) => void;
}) {
  if (loading) return <Skeleton className="h-64" />;
  if (error) return <ErrorBanner message={error.message} correlationId={error.correlationId} />;

  if (headers && headers.length === 0) {
    return <p className="rounded-xl border border-subtle bg-surface p-10 text-center text-sm text-muted">This inbox is empty.</p>;
  }

  return (
    <div className="overflow-hidden rounded-xl border border-subtle bg-surface">
      {headers?.map((h) => (
        <button
          key={h.uid}
          type="button"
          onClick={() => onSelect(h.uid)}
          className="flex w-full items-center gap-3 border-b border-subtle px-4 py-3 text-left transition-colors last:border-b-0 hover:bg-surface-sunken"
        >
          {h.seen ? (
            <span className="size-2 shrink-0" aria-hidden />
          ) : (
            <span className="size-2 shrink-0 rounded-full bg-primary" aria-label="Unread" />
          )}
          <span className={cn("w-44 shrink-0 truncate text-sm", h.seen ? "text-text" : "font-semibold text-text")}>
            {h.fromName || h.fromAddress}
          </span>
          <span className={cn("min-w-0 flex-1 truncate text-sm", h.seen ? "text-muted" : "text-text")}>
            {h.subject}
          </span>
          {h.hasAttachments && <Paperclip className="size-3.5 shrink-0 text-muted" aria-hidden />}
          <span className="shrink-0 text-xs text-muted">{when(h.date)}</span>
        </button>
      ))}
    </div>
  );
}

function MessageView({ query, onBack, onReply }: {
  query: { data?: MailMessage; isPending: boolean; error: unknown };
  onBack: () => void;
  onReply: (msg: MailMessage) => void;
}) {
  return (
    <div className="space-y-3">
      <Button variant="ghost" size="sm" onClick={onBack}>
        <ArrowLeft className="size-4" />
        Back to inbox
      </Button>

      {query.error ? (
        <ErrorBanner
          message={(query.error as ApiError).message}
          correlationId={(query.error as ApiError).correlationId}
        />
      ) : query.isPending || !query.data ? (
        <Skeleton className="h-64" />
      ) : (
        <div className="rounded-xl border border-subtle bg-surface">
          <div className="border-b border-subtle p-4">
            <div className="flex items-start justify-between gap-3">
              <h2 className="text-lg font-semibold text-text">{query.data.subject}</h2>
              <Button variant="secondary" size="sm" onClick={() => onReply(query.data!)}>
                <Reply className="size-4" />
                Reply
              </Button>
            </div>
            <p className="mt-2 text-sm text-text">
              <span className="font-medium">{query.data.fromName || query.data.fromAddress}</span>{" "}
              {query.data.fromName && <span className="text-muted">&lt;{query.data.fromAddress}&gt;</span>}
            </p>
            <p className="text-xs text-muted">
              To: {query.data.to || "—"} · {new Date(query.data.date).toLocaleString()}
            </p>
          </div>

          <div className="p-1">
            {query.data.isHtml ? (
              <HtmlMessage html={query.data.body} />
            ) : (
              <pre className="whitespace-pre-wrap p-4 font-sans text-sm text-text">{query.data.body}</pre>
            )}
          </div>
        </div>
      )}
    </div>
  );
}

/**
 * An email's HTML, in an iframe that grows to its content so the page scrolls as one — not a box that
 * scrolls inside the page. `sandbox="allow-same-origin"` (and crucially no `allow-scripts`) means the
 * message cannot run JavaScript, while the parent can still read its height to size the frame.
 */
function HtmlMessage({ html }: { html: string }) {
  const ref = useRef<HTMLIFrameElement>(null);
  const [height, setHeight] = useState(320);

  const measure = () => {
    const doc = ref.current?.contentDocument;
    if (doc?.body) {
      setHeight(Math.max(doc.body.scrollHeight, doc.documentElement.scrollHeight) + 16);
    }
  };

  useEffect(() => {
    // Images that load after the frame does grow the document — re-measure a few times to catch them.
    const timers = [200, 700, 1600].map((delay) => window.setTimeout(measure, delay));
    return () => timers.forEach(window.clearTimeout);
  }, [html]);

  return (
    <iframe
      ref={ref}
      title="Message"
      sandbox="allow-same-origin"
      srcDoc={html}
      onLoad={measure}
      style={{ height }}
      className="w-full rounded-lg border-0 bg-white"
    />
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
