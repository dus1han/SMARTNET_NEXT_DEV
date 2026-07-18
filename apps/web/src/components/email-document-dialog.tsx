"use client";

/**
 * Emails a document to the customer's saved contacts.
 *
 * The server owns everything shown here: the contact list, the covering message, and whether sending is
 * possible at all. `blocked` is reported before the user picks anybody — an unconfigured mail server or
 * the company's send switch being off is worth saying up front, not after pressing Send.
 *
 * Document-agnostic on purpose: a job sheet and a quotation differ in what the server puts in the
 * message, not in how the choosing works.
 */

import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import type { ApiError } from "@/lib/api";
import { Button, Checkbox, Dialog, ErrorBanner, Skeleton, toast } from "@/components/ui";

/** What the server offers for a document — the shape every document's recipients endpoint returns. */
export interface DocumentRecipients {
  contacts: { id: number; name?: string | null; email: string; usage: string; selected: boolean }[];
  subject: string;
  body: string;
  attachmentName: string;
  blocked?: string | null;
}

export interface EmailResult {
  sent: boolean;
  recipients: string[];
  error?: string | null;
}

export function EmailDocumentDialog({
  open,
  onOpenChange,
  documentId,
  documentLabel,
  queryKey,
  fetchRecipients,
  send,
  onSent,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;

  documentId: number;

  /** How the document is named in the dialog, e.g. "Quotation SNQ-1551". */
  documentLabel: string;

  /** Cache key prefix, so one document's recipients are not served for another. */
  queryKey: string;

  fetchRecipients: (id: number) => Promise<DocumentRecipients>;
  send: (id: number, contactIds: number[]) => Promise<EmailResult>;

  onSent?: () => void;
}) {
  const recipients = useQuery({
    queryKey: [queryKey, documentId, "recipients"],
    queryFn: () => fetchRecipients(documentId),
    enabled: open,
  });

  const [picked, setPicked] = useState<Set<number> | null>(null);
  const [sending, setSending] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  const contacts = recipients.data?.contacts ?? [];
  const blocked = recipients.data?.blocked ?? null;

  // Until the user touches anything, the selection is the server's default — the document contacts.
  const selected = picked ?? new Set(contacts.filter((c) => c.selected).map((c) => c.id));

  function toggle(id: number) {
    const next = new Set(selected);
    if (next.has(id)) next.delete(id);
    else next.add(id);
    setPicked(next);
  }

  async function submit() {
    setSending(true);
    setError(null);
    try {
      const result = await send(documentId, [...selected]);

      // A refusal by the mail server comes back as 200 with sent=false — shown as a refusal, with the
      // server's own reason, rather than a generic failure.
      if (result.sent) {
        toast.success(
          `${documentLabel} sent to ${result.recipients.length === 1 ? result.recipients[0] : `${result.recipients.length} contacts`}.`,
        );
        onOpenChange(false);
        onSent?.();
      } else {
        toast.error(result.error ?? `${documentLabel} could not be sent.`);
      }
    } catch (e) {
      setError(e as ApiError);
    } finally {
      setSending(false);
    }
  }

  return (
    <Dialog
      open={open}
      onOpenChange={onOpenChange}
      title={`Email ${documentLabel.toLowerCase()}`}
      description={`Sends ${documentLabel} as a PDF attachment to the contacts you choose.`}
      footer={
        <>
          <Button variant="secondary" onClick={() => onOpenChange(false)} disabled={sending}>Cancel</Button>
          <Button
            onClick={submit}
            pending={sending}
            disabled={selected.size === 0 || blocked !== null || recipients.isPending}
          >
            Send
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        {error && <ErrorBanner message={error.message} correlationId={error.correlationId} />}
        {recipients.isPending && <Skeleton className="h-32" />}

        {blocked && <p className="rounded-md bg-warning/10 p-3 text-sm text-warning">{blocked}</p>}

        {contacts.length > 0 && (
          <div className="space-y-2">
            <p className="stat-label text-xs font-semibold uppercase tracking-wider">Send to</p>
            {contacts.map((contact) => (
              <Checkbox
                key={contact.id}
                checked={selected.has(contact.id)}
                onChange={() => toggle(contact.id)}
                label={contact.name?.trim() ? `${contact.name} — ${contact.email}` : contact.email}
                hint={contact.usage === "NotificationsOnly" ? "Notifications only" : undefined}
              />
            ))}
          </div>
        )}

        {recipients.data && (
          <div className="space-y-2 rounded-md border border-subtle p-3">
            <p className="stat-label text-xs font-semibold uppercase tracking-wider">Message</p>
            <p className="text-sm font-medium text-text">{recipients.data.subject}</p>
            <div
              className="text-sm text-muted [&_p]:mb-2"
              // The body is this app's own fixed template, built server-side — not user input, and not
              // anything the customer supplied.
              dangerouslySetInnerHTML={{ __html: recipients.data.body }}
            />
            <p className="text-xs text-muted">Attachment: {recipients.data.attachmentName}</p>
          </div>
        )}
      </div>
    </Dialog>
  );
}
