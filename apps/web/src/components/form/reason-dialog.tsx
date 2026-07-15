"use client";

import { useState } from "react";
import { MINIMUM_REASON_LENGTH } from "@/lib/admin";
import { Button, Dialog, Textarea } from "@/components/ui";

export interface ReasonRequest {
  title: string;
  description?: string;
  confirmLabel?: string;
  destructive?: boolean;
  onConfirm: (reason: string) => void | Promise<void>;
}

/**
 * Asks why.
 *
 * AUDIT.md §5 makes a reason mandatory on the changes that matter — editing an issued invoice,
 * deleting anything, changing permissions, resetting a password, changing tax rates or numbering.
 * The server rejects those requests without an `X-Change-Reason` header, so this is not a courtesy
 * dialog: it is how the request gets made at all.
 *
 * <p>It lives here, once, rather than as a reason textbox copy-pasted onto every screen — which is
 * how one of them ends up with a five-character minimum, or none.</p>
 *
 * <p>⚠️ The spec's own warning, worth repeating where somebody might widen this: demand a reason for
 * every keystroke and staff will type "." forever. That looks like an audit trail and isn't, which
 * is worse than having none — because people trust it.</p>
 */
export function ReasonDialog({ request, onClose }: {
  request: ReasonRequest | null;
  onClose: () => void;
}) {
  const [reason, setReason] = useState("");
  const [pending, setPending] = useState(false);

  const trimmed = reason.trim();

  // The same rule the server applies. Checking it here saves a round trip that comes back 400; it
  // does not replace the check, and must not be relaxed on the assumption that it does.
  const valid = trimmed.length >= MINIMUM_REASON_LENGTH;

  const close = () => {
    setReason("");
    setPending(false);
    onClose();
  };

  return (
    <Dialog
      open={request !== null}
      onOpenChange={(open) => !open && close()}
      title={request?.title ?? ""}
      description={request?.description}
      footer={
        <>
          <Button variant="ghost" onClick={close} disabled={pending}>
            Cancel
          </Button>

          <Button
            variant={request?.destructive ? "danger" : "primary"}
            disabled={!valid}
            pending={pending}
            onClick={async () => {
              if (!request || !valid) return;

              setPending(true);

              try {
                await request.onConfirm(trimmed);
                close();
              } finally {
                setPending(false);
              }
            }}
          >
            {request?.confirmLabel ?? "Confirm"}
          </Button>
        </>
      }
    >
      <Textarea
        label="Reason for this change"
        placeholder="What changed, and why? This is recorded against your name."
        value={reason}
        onChange={(e) => setReason(e.target.value)}
        required
        autoFocus
        hint={
          trimmed.length > 0 && !valid
            ? `${MINIMUM_REASON_LENGTH - trimmed.length} more character${
                MINIMUM_REASON_LENGTH - trimmed.length === 1 ? "" : "s"
              } needed.`
            : "Whoever reads the audit log in a year will only have this sentence."
        }
      />
    </Dialog>
  );
}

/**
 * Wires the dialog up.
 *
 * ```tsx
 * const reason = useReason();
 * <Button onClick={() => reason.ask({ title: "Disable user", destructive: true,
 *   onConfirm: (why) => disableUser(id, why) })}>Disable</Button>
 * {reason.dialog}
 * ```
 */
export function useReason() {
  const [request, setRequest] = useState<ReasonRequest | null>(null);

  return {
    ask: setRequest,
    dialog: <ReasonDialog request={request} onClose={() => setRequest(null)} />,
  };
}
