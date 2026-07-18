"use client";

/**
 * Previews a server-rendered PDF and prints it.
 *
 * The document is fetched as a blob rather than pointed at with `<iframe src="…/pdf">`, for the same
 * reason the Excel export is: the auth cookie is httpOnly and the API is a different origin, so a
 * plain navigation is not guaranteed to carry it. Fetching with `credentials: "include"` is, and the
 * resulting blob URL is same-origin — which is also what makes `contentWindow.print()` legal.
 *
 * Printing is the point. A job sheet is signed on collection, so the person raising the job needs
 * paper in hand, and "download it, find it, open it, print it" is three steps too many.
 */

import { useEffect, useRef, useState } from "react";
import { Printer } from "lucide-react";
import { API_BASE_URL, getActiveCompany } from "@/lib/api";
import { Button, Dialog, ErrorBanner, Skeleton } from "@/components/ui";

export function PrintPreview({ open, onOpenChange, path, title, onLoaded }: {
  open: boolean;
  onOpenChange: (open: boolean) => void;

  /** API path of the PDF, e.g. `/api/job-cards/8/pdf`. */
  path: string;

  title: string;

  /** Called once the document has been fetched — the audit event has been recorded by then. */
  onLoaded?: () => void;
}) {
  const [url, setUrl] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const frame = useRef<HTMLIFrameElement>(null);

  useEffect(() => {
    if (!open) return;

    let revoked: string | null = null;
    let cancelled = false;

    (async () => {
      setError(null);
      setUrl(null);

      try {
        const company = getActiveCompany();
        const response = await fetch(`${API_BASE_URL}${path}`, {
          credentials: "include",
          headers: company === null ? {} : { "X-Company-Id": String(company) },
        });

        if (!response.ok) throw new Error("The document could not be produced.");

        const blob = await response.blob();
        if (cancelled) return;

        revoked = URL.createObjectURL(blob);
        setUrl(revoked);
        onLoaded?.();
      } catch (e) {
        if (!cancelled) setError(e instanceof Error ? e.message : "The document could not be produced.");
      }
    })();

    return () => {
      cancelled = true;
      // Revoked on close, not after the iframe loads — the viewer keeps reading from the URL.
      if (revoked) URL.revokeObjectURL(revoked);
    };
    // onLoaded is intentionally excluded: it is a callback identity, not a fetch input.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, path]);

  function print() {
    // The blob is same-origin, so reaching into the frame is allowed. Focus first — without it a
    // background frame's print() is a no-op in some browsers.
    const win = frame.current?.contentWindow;
    win?.focus();
    win?.print();
  }

  return (
    <Dialog
      open={open}
      onOpenChange={onOpenChange}
      title={title}
      description="Check it reads correctly, then print."
      size="lg"
      footer={
        <>
          <Button variant="secondary" onClick={() => onOpenChange(false)}>Close</Button>
          <Button onClick={print} disabled={url === null}>
            <Printer />
            Print
          </Button>
        </>
      }
    >
      {error && <ErrorBanner message={error} />}
      {!error && url === null && <Skeleton className="h-[60vh]" />}
      {url !== null && (
        <iframe
          ref={frame}
          // The viewer's own chrome, turned off: this dialog supplies the Print button, so the
          // built-in toolbar and page-thumbnail sidebar are a second set of controls competing with
          // it. These are PDF open parameters, honoured by the Chromium viewer.
          src={`${url}#toolbar=0&navpanes=0&statusbar=0&view=FitH`}
          title={title}
          className="h-[60vh] w-full rounded-md border border-subtle"
        />
      )}
    </Dialog>
  );
}
