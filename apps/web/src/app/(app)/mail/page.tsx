"use client";

/**
 * Mail — the signed-in user's own mailboxes, worked from inside the app.
 *
 * A compact mailbox picker (a dropdown, not a column), a folder bar, an inbox list, and — when a message is
 * opened — a full-width reading view with reply / forward / mark-unread / delete. Reading is over IMAP;
 * sending is over SMTP as that mailbox, with a copy filed in Sent. Everything is scoped server-side to the
 * caller's assigned mailboxes, so this screen never names a user id. The page scrolls like every other one.
 */

import { useEffect, useRef, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import * as Menu from "@radix-ui/react-dropdown-menu";
import {
  AlertTriangle,
  ArrowLeft,
  ChevronDown,
  Download,
  Forward,
  Inbox,
  Mail,
  MailOpen,
  Paperclip,
  PenSquare,
  RefreshCw,
  Reply,
  Search,
  Send,
  Trash2,
  X,
} from "lucide-react";
import type { MailFolder, MailHeader, MailboxListItem, MailMessage } from "@smartnet/api-client";
import { ApiError } from "@/lib/api";
import {
  attachmentUrl,
  deleteMessage,
  listFolders,
  listMessages,
  listMyMailboxes,
  readMessage,
  sendMail,
  setSeen,
} from "@/lib/mail";
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
  cc: string;
  bcc: string;
  subject: string;
  body: string;
  files: File[];
}

const PAGE = 40;
const MAX_MESSAGES = 200;

export default function MailPage() {
  const queryClient = useQueryClient();

  // Polled so new mail and changing unread counts appear on their own. React Query pauses these while the
  // tab is hidden (refetchIntervalInBackground defaults off), so an idle tab is not hammering IMAP.
  const mailboxes = useQuery({ queryKey: ["my-mailboxes"], queryFn: listMyMailboxes, refetchInterval: 120_000 });

  const [picked, setPicked] = useState<number | null>(null);
  const [folder, setFolder] = useState("INBOX");
  const [uid, setUid] = useState<number | null>(null);
  const [take, setTake] = useState(PAGE);
  const [query, setQuery] = useState(""); // the search box text
  const [applied, setApplied] = useState(""); // the search actually running
  const [compose, setCompose] = useState<Compose | null>(null);

  // Selected mailbox, defaulting to the first once the list arrives — derived, not set from an effect.
  const mailboxId = picked ?? mailboxes.data?.[0]?.id ?? null;
  const activeMailbox = mailboxes.data?.find((m) => m.id === mailboxId) ?? null;

  const folders = useQuery({
    queryKey: ["folders", mailboxId],
    queryFn: () => listFolders(mailboxId!),
    enabled: mailboxId !== null,
    refetchInterval: 120_000,
  });

  const messages = useQuery({
    queryKey: ["messages", mailboxId, folder, take, applied],
    queryFn: () => listMessages(mailboxId!, folder, take, applied || undefined),
    enabled: mailboxId !== null,
    refetchInterval: 60_000,
  });

  const openMsg = useQuery({
    queryKey: ["message", mailboxId, folder, uid],
    queryFn: () => readMessage(mailboxId!, folder, uid!),
    enabled: mailboxId !== null && uid !== null,
    staleTime: Infinity, // a read message does not change, and opening it already marked it seen
  });

  const refreshLists = () => {
    void queryClient.invalidateQueries({ queryKey: ["my-mailboxes"] });
    void queryClient.invalidateQueries({ queryKey: ["folders", mailboxId] });
    void queryClient.invalidateQueries({ queryKey: ["messages", mailboxId] });
  };

  // Opening a message marks it read on the server — reflect that in the badges and the list.
  useEffect(() => {
    if (openMsg.data) refreshLists();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [openMsg.data?.uid]);

  const resetView = () => {
    setUid(null);
    setTake(PAGE);
    setQuery("");
    setApplied("");
  };

  const selectMailbox = (id: number) => {
    setPicked(id);
    setFolder("INBOX");
    resetView();
  };

  const selectFolder = (full: string) => {
    setFolder(full);
    resetView();
  };

  const send = useMutation({
    mutationFn: (draft: Compose) => sendMail(mailboxId!, draft),
    onSuccess: () => {
      toast.success("Message sent.");
      setCompose(null);
      refreshLists();
    },
    onError: (error: unknown) => toast.error(message(error)),
  });

  const markUnread = useMutation({
    mutationFn: () => setSeen(mailboxId!, folder, uid!, false),
    onSuccess: () => {
      toast.success("Marked unread.");
      setUid(null);
      refreshLists();
    },
    onError: (error: unknown) => toast.error(message(error)),
  });

  const remove = useMutation({
    mutationFn: () => deleteMessage(mailboxId!, folder, uid!),
    onSuccess: () => {
      toast.success("Message deleted.");
      setUid(null);
      refreshLists();
    },
    onError: (error: unknown) => toast.error(message(error)),
  });

  const startReply = (msg: MailMessage) =>
    setCompose({
      to: msg.fromAddress,
      cc: "",
      bcc: "",
      subject: prefixed(msg.subject, "re:", "Re: "),
      body: `\n\n----- On ${when(msg.date)}, ${msg.fromName || msg.fromAddress} wrote -----\n${quote(msg)}`,
      files: [],
    });

  const startForward = (msg: MailMessage) =>
    setCompose({
      to: "",
      cc: "",
      bcc: "",
      subject: prefixed(msg.subject, "fwd:", "Fwd: "),
      body: `\n\n----- Forwarded message -----\nFrom: ${msg.fromName || msg.fromAddress}\nDate: ${new Date(
        msg.date,
      ).toLocaleString()}\nSubject: ${msg.subject}\nTo: ${msg.to}\n\n${quote(msg)}`,
      files: [],
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
          <Button onClick={() => setCompose({ to: "", cc: "", bcc: "", subject: "", body: "", files: [] })} disabled={mailboxId === null}>
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
              <Button variant="ghost" size="icon" onClick={() => messages.refetch()} aria-label="Refresh">
                <RefreshCw className="size-4" />
              </Button>
            )}
          </div>

          {uid === null && <FolderBar folders={folders.data} selected={folder} onSelect={selectFolder} />}

          {uid === null && (
            <form
              className="flex items-center gap-2"
              onSubmit={(e) => {
                e.preventDefault();
                setApplied(query.trim());
                setTake(PAGE);
              }}
            >
              <div className="relative min-w-0 flex-1 sm:max-w-md">
                <Search className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-muted" aria-hidden />
                <input
                  value={query}
                  onChange={(e) => setQuery(e.target.value)}
                  placeholder="Search this folder…"
                  className="h-10 w-full rounded-lg border border-subtle bg-surface pl-9 pr-9 text-sm text-text outline-none focus-visible:ring-2 focus-visible:ring-ring/25"
                />
                {applied && (
                  <button
                    type="button"
                    aria-label="Clear search"
                    onClick={() => {
                      setQuery("");
                      setApplied("");
                      setTake(PAGE);
                    }}
                    className="absolute right-2.5 top-1/2 -translate-y-1/2 text-muted hover:text-text"
                  >
                    <X className="size-4" />
                  </button>
                )}
              </div>
              <Button type="submit" variant="secondary" size="sm">
                Search
              </Button>
            </form>
          )}

          {uid === null ? (
            <>
              {applied && (
                <p className="text-xs text-muted">
                  Results for “{applied}”. <button type="button" className="text-primary hover:underline" onClick={() => { setQuery(""); setApplied(""); }}>Clear</button>
                </p>
              )}
              <MessageList
                headers={messages.data}
                loading={messages.isPending && mailboxId !== null}
                error={messages.error as ApiError | null}
                onSelect={setUid}
              />
              {messages.data && messages.data.length >= take && take < MAX_MESSAGES && (
                <div className="flex justify-center">
                  <Button
                    variant="secondary"
                    size="sm"
                    pending={messages.isFetching}
                    onClick={() => setTake((t) => Math.min(t + PAGE, MAX_MESSAGES))}
                  >
                    Load more
                  </Button>
                </div>
              )}
            </>
          ) : (
            <MessageView
              query={openMsg}
              mailboxId={mailboxId!}
              folder={folder}
              onBack={() => setUid(null)}
              onReply={startReply}
              onForward={startForward}
              onMarkUnread={() => markUnread.mutate()}
              onDelete={() => remove.mutate()}
              busy={markUnread.isPending || remove.isPending}
            />
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

function prefixed(subject: string, lower: string, prefix: string) {
  return subject.toLowerCase().startsWith(lower) ? subject : `${prefix}${subject}`;
}

/** The original quoted with "> " prefixes — the server's plain-text rendering, so a formatted mail quotes too. */
function quote(msg: MailMessage) {
  return (msg.text || msg.body)
    .split("\n")
    .map((line) => `> ${line}`)
    .join("\n");
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

function FolderBar({ folders, selected, onSelect }: {
  folders: MailFolder[] | undefined;
  selected: string;
  onSelect: (fullName: string) => void;
}) {
  if (!folders || folders.length <= 1) return null;

  return (
    <div className="flex flex-wrap gap-1.5">
      {folders.map((f) => {
        const active = f.fullName === selected;
        return (
          <button
            key={f.fullName}
            type="button"
            onClick={() => onSelect(f.fullName)}
            className={cn(
              "flex items-center gap-1.5 rounded-lg px-3 py-1.5 text-sm transition-colors",
              active ? "bg-primary text-white" : "border border-subtle bg-surface text-text hover:bg-surface-sunken",
            )}
          >
            {f.name}
            {f.unread > 0 && (
              <span
                className={cn(
                  "rounded-full px-1.5 text-xs font-semibold tabular-nums",
                  active ? "bg-white/25 text-white" : "bg-primary text-white",
                )}
              >
                {f.unread}
              </span>
            )}
          </button>
        );
      })}
    </div>
  );
}

function MessageList({ headers, loading, error, onSelect }: {
  headers: MailHeader[] | undefined;
  loading: boolean;
  error: ApiError | null;
  onSelect: (uid: number) => void;
}) {
  if (loading) return <Skeleton className="h-64" />;
  if (error) return <ErrorBanner message={error.message} correlationId={error.correlationId} />;

  if (headers && headers.length === 0) {
    return <p className="rounded-xl border border-subtle bg-surface p-10 text-center text-sm text-muted">This folder is empty.</p>;
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
          <span className={cn("min-w-0 flex-1 truncate text-sm", h.seen ? "text-muted" : "text-text")}>{h.subject}</span>
          {h.hasAttachments && <Paperclip className="size-3.5 shrink-0 text-muted" aria-hidden />}
          <span className="shrink-0 text-xs text-muted">{when(h.date)}</span>
        </button>
      ))}
    </div>
  );
}

function MessageView({ query, mailboxId, folder, onBack, onReply, onForward, onMarkUnread, onDelete, busy }: {
  query: { data?: MailMessage; isPending: boolean; error: unknown };
  mailboxId: number;
  folder: string;
  onBack: () => void;
  onReply: (msg: MailMessage) => void;
  onForward: (msg: MailMessage) => void;
  onMarkUnread: () => void;
  onDelete: () => void;
  busy: boolean;
}) {
  return (
    <div className="space-y-3">
      <Button variant="ghost" size="sm" onClick={onBack}>
        <ArrowLeft className="size-4" />
        Back
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
            <div className="flex flex-wrap items-start justify-between gap-3">
              <h2 className="text-lg font-semibold text-text">{query.data.subject}</h2>
              <div className="flex flex-wrap gap-2">
                <Button variant="secondary" size="sm" onClick={() => onReply(query.data!)}>
                  <Reply className="size-4" />
                  Reply
                </Button>
                <Button variant="ghost" size="sm" onClick={() => onForward(query.data!)}>
                  <Forward className="size-4" />
                  Forward
                </Button>
                <Button variant="ghost" size="sm" onClick={onMarkUnread} disabled={busy}>
                  <MailOpen className="size-4" />
                  Unread
                </Button>
                <Button variant="ghost" size="sm" onClick={onDelete} disabled={busy} className="text-danger">
                  <Trash2 className="size-4" />
                  Delete
                </Button>
              </div>
            </div>
            <p className="mt-2 text-sm text-text">
              <span className="font-medium">{query.data.fromName || query.data.fromAddress}</span>{" "}
              {query.data.fromName && <span className="text-muted">&lt;{query.data.fromAddress}&gt;</span>}
            </p>
            <p className="text-xs text-muted">
              To: {query.data.to || "—"} · {new Date(query.data.date).toLocaleString()}
            </p>

            {query.data.attachments.length > 0 && (
              <div className="mt-3 flex flex-wrap gap-2">
                {query.data.attachments.map((a) => (
                  <a
                    key={a.index}
                    href={attachmentUrl(mailboxId, folder, query.data!.uid, a.index)}
                    download={a.fileName}
                    className="inline-flex max-w-full items-center gap-1.5 rounded-lg border border-subtle bg-surface-sunken px-2.5 py-1.5 text-xs text-text transition-colors hover:bg-surface-sunken/70"
                  >
                    <Paperclip className="size-3.5 shrink-0 text-muted" aria-hidden />
                    <span className="truncate">{a.fileName}</span>
                    <Download className="size-3.5 shrink-0 text-muted" aria-hidden />
                  </a>
                ))}
              </div>
            )}
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
  const [showCc, setShowCc] = useState(false);
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
          <div>
            <div className="mb-1.5 flex items-center justify-between">
              <span className="text-sm font-medium text-text">To</span>
              {!showCc && (
                <button
                  type="button"
                  className="text-xs font-medium text-primary hover:underline"
                  onClick={() => setShowCc(true)}
                >
                  Cc / Bcc
                </button>
              )}
            </div>
            <input
              placeholder="name@example.com, another@example.com"
              value={draft.to}
              onChange={(e) => onChange({ ...draft, to: e.target.value })}
              className="h-10 w-full rounded-lg border border-subtle bg-surface px-3 text-sm text-text outline-none focus-visible:ring-2 focus-visible:ring-ring/25"
            />
          </div>

          {showCc && (
            <>
              <Input label="Cc" value={draft.cc} onChange={(e) => onChange({ ...draft, cc: e.target.value })} />
              <Input label="Bcc" value={draft.bcc} onChange={(e) => onChange({ ...draft, bcc: e.target.value })} />
            </>
          )}

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

          <div>
            <label className="inline-flex cursor-pointer items-center gap-1.5 rounded-lg border border-subtle bg-surface px-3 py-1.5 text-sm text-text transition-colors hover:bg-surface-sunken">
              <Paperclip className="size-4 text-muted" aria-hidden />
              Attach files
              <input
                type="file"
                multiple
                className="hidden"
                onChange={(e) => {
                  const chosen = Array.from(e.target.files ?? []);
                  if (chosen.length) onChange({ ...draft, files: [...draft.files, ...chosen] });
                  e.target.value = ""; // let the same file be picked again after removal
                }}
              />
            </label>

            {draft.files.length > 0 && (
              <ul className="mt-2 space-y-1">
                {draft.files.map((file, i) => (
                  <li
                    key={`${file.name}-${i}`}
                    className="flex items-center gap-2 rounded-md bg-surface-sunken px-2.5 py-1.5 text-xs text-text"
                  >
                    <Paperclip className="size-3.5 shrink-0 text-muted" aria-hidden />
                    <span className="min-w-0 flex-1 truncate">{file.name}</span>
                    <span className="shrink-0 text-muted">{Math.ceil(file.size / 1024)} KB</span>
                    <button
                      type="button"
                      aria-label={`Remove ${file.name}`}
                      className="shrink-0 text-muted hover:text-danger"
                      onClick={() => onChange({ ...draft, files: draft.files.filter((_, idx) => idx !== i) })}
                    >
                      <X className="size-3.5" />
                    </button>
                  </li>
                ))}
              </ul>
            )}
          </div>
        </div>
      )}
    </Dialog>
  );
}
