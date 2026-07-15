"use client";

/**
 * THE LINE-ITEM EDITOR PROTOTYPE — Phase 2, slice 7.
 *
 * This is not a feature. It is the parent plan's risk register, executed:
 *
 *   > "The cart rewrite (Phase 5) changes how staff enter documents — prototype the line-item editor
 *   > in Phase 2 and put it in front of a real user before Phase 5."
 *
 * The single biggest behavioural change in the whole migration is that the server-side session cart
 * (`addtoCart` / `cartLoad` / `removeQItem`) is deleted and the browser holds the draft. Everything
 * else in the rebuild is invisible to the person doing the typing. *This* they will feel on the
 * first invoice — and if it is slower than what they have now, they will be right to hate it.
 *
 * So this screen exists to be sat in front of whoever types invoices all day, and the deliverable is
 * **what they say**, not what it looks like. It saves nothing, posts nothing, and is deleted when
 * Phase 5 builds the real thing.
 */

import { useEffect, useReducer, useRef, useState } from "react";
import { FlaskConical, Keyboard, Package, PenLine, Trash2 } from "lucide-react";
import { PageHeader } from "@/components/shell/app-shell";
import {
  LineItems,
  draftReducer,
  emptyDraft,
  toPayload,
  type DocumentKind,
  type DraftDocument,
} from "@/components/line-items";
import { Badge, Button, Card, Dialog, Input, toast } from "@/components/ui";
import { cn } from "@/lib/cn";

/**
 * The draft survives a refresh, and it belongs to this browser — and to this document.
 *
 * Note what this replaces. The legacy cart lives in the **server session**, which means the draft is
 * keyed by who you are rather than by which document you are typing: open an item invoice and a
 * service invoice in two tabs today and they share one cart and quietly poison each other. A draft in
 * the browser, keyed by the document, cannot do that.
 */
const draftKey = (kind: DocumentKind) => `smartnet.prototype.draft.${kind}`;

export default function LineItemEditorPrototype() {
  const [kind, setKind] = useState<DocumentKind>("service");

  return (
    <div className="space-y-6">
      <PageHeader
        title="Line-item editor"
        description="A prototype of how invoices will be typed in the new system. Nothing here is saved, and no item on it is real stock."
        actions={
          <Badge tone="warning">
            <FlaskConical className="mr-1 inline size-3" aria-hidden />
            Prototype
          </Badge>
        }
      />

      <DocumentPicker kind={kind} onChange={setKind} />

      {/* Keyed by the document: switching gives you *that* document's draft, untouched, rather than
          carrying item lines into a service invoice. Two paths, one engine — the difference between
          them is where a line may come from, and nothing else. */}
      <Editor key={kind} kind={kind} />
    </div>
  );
}

/**
 * The two paths, chosen up front.
 *
 * The business keeps them separate, and that is deliberate: an item invoice is raised from the item
 * master, a service invoice is typed, and they are different jobs done by different people. Merging
 * them into one screen with a mode people have to notice is how a typed line ends up on an item
 * invoice. So the choice is made here, before any typing, and never again.
 */
function DocumentPicker({ kind, onChange }: {
  kind: DocumentKind;
  onChange: (kind: DocumentKind) => void;
}) {
  const options = [
    {
      value: "service" as const,
      icon: PenLine,
      title: "Service invoice",
      detail: "Lines are typed. This is what the business raises today — every line in the database is one.",
    },
    {
      value: "item" as const,
      icon: Package,
      title: "Item invoice",
      detail: "Lines come from the item master, with the price already on them. Not in use today.",
    },
  ];

  return (
    <div className="grid gap-3 sm:grid-cols-2">
      {options.map((option) => {
        const selected = kind === option.value;

        return (
          <button
            key={option.value}
            type="button"
            aria-pressed={selected}
            onClick={() => onChange(option.value)}
            className={cn(
              "flex gap-3 rounded-lg border p-4 text-left",
              "transition-colors duration-200 ease-out",
              selected
                ? "border-primary bg-primary-ghost"
                : "border-subtle bg-surface hover:border-strong",
            )}
          >
            <option.icon
              className={cn("mt-0.5 size-5 shrink-0", selected ? "text-primary" : "text-muted")}
              aria-hidden
            />

            <span>
              <span className={cn("block font-medium", selected ? "text-primary" : "text-text")}>
                {option.title}
              </span>
              <span className="mt-1 block text-sm text-muted">{option.detail}</span>
            </span>
          </button>
        );
      })}
    </div>
  );
}

function Editor({ kind }: { kind: DocumentKind }) {
  const [draft, dispatch] = useReducer(draftReducer, emptyDraft);
  const [payload, setPayload] = useState<string | null>(null);

  /** Prototype instrumentation — see the panel below. */
  const [lineOperations, setLineOperations] = useState(0);

  /**
   * Declared *before* the restore effect on purpose.
   *
   * Effects run in declaration order, so on the first commit this one runs while the flag is still
   * false and writes nothing — and the restore below then has an untouched localStorage to read
   * from. Written the other way round, the empty initial draft is saved over the stored one in the
   * instant before the restore lands, and a refresh silently eats the user's lines.
   */
  const hydrated = useRef(false);

  useEffect(() => {
    if (hydrated.current) window.localStorage.setItem(draftKey(kind), JSON.stringify(draft));
  }, [draft, kind]);

  // Restore after mount, not during render: localStorage does not exist on the server, and reading
  // it while rendering is how a Next.js app gets a hydration mismatch.
  useEffect(() => {
    const stored = window.localStorage.getItem(draftKey(kind));

    if (stored) {
      try {
        dispatch({ type: "restore", draft: JSON.parse(stored) as DraftDocument });
      } catch {
        window.localStorage.removeItem(draftKey(kind));
      }
    }

    hydrated.current = true;
  }, [kind]);

  return (
    <div className="space-y-6">
      <Card>
        <div className="grid gap-4 sm:grid-cols-2">
          <Input
            label="Customer"
            placeholder="Who is this for?"
            value={draft.customer}
            onChange={(event) => dispatch({ type: "header", patch: { customer: event.target.value } })}
          />

          <Input
            label="Their PO reference"
            placeholder="Optional"
            value={draft.reference}
            onChange={(event) => dispatch({ type: "header", patch: { reference: event.target.value } })}
          />
        </div>

        <div className="mt-5">
          <LineItems
            kind={kind}
            lines={draft.lines}
            dispatch={(action) => {
              // Every add and every remove is one server round trip in the legacy app, plus a
              // second one to redraw the grid. Counting them is the point of this screen.
              if (action.type === "add" || action.type === "remove") {
                setLineOperations((count) => count + 1);
              }

              dispatch(action);
            }}
          />
        </div>

        <div className="mt-5 flex flex-wrap justify-end gap-2">
          <Button
            variant="ghost"
            disabled={draft.lines.length === 0}
            onClick={() => {
              dispatch({ type: "clear" });
              setLineOperations(0);
              toast.success("Draft cleared.");
            }}
          >
            <Trash2 />
            Clear draft
          </Button>

          <Button
            disabled={draft.lines.length === 0}
            onClick={() => setPayload(JSON.stringify(toPayload(draft), null, 2))}
          >
            Save invoice
          </Button>
        </div>
      </Card>

      <div className="grid gap-4 lg:grid-cols-2">
        <RoundTrips lineOperations={lineOperations} />
        <Shortcuts kind={kind} />
      </div>

      <Card className="border-warning/40 bg-warning-subtle">
        <h2 className="text-sm font-medium text-warning-text">What we are actually asking you</h2>

        <ul className="mt-2 list-disc space-y-1 pl-5 text-sm text-warning-text/90">
          <li>Type a real invoice you did this week. Time yourself, and time the old screen.</li>
          <li>Did your hand ever have to go to the mouse? Where?</li>
          <li>
            The <strong>service invoice</strong> is the one you raise every day — start there. Then
            try the <strong>item invoice</strong>, which you do not use today, and tell us why not.
            That answer decides whether the item master is worth reviving.
          </li>
          <li>What did the old screen let you do that this one does not?</li>
        </ul>

        <p className="mt-3 max-w-prose text-sm text-warning-text/80">
          If this is slower than what you have now, say so. It is far cheaper to hear it today than
          after Phase 5 has been built on top of it.
        </p>
      </Card>

      <Dialog
        open={payload !== null}
        onOpenChange={(open) => !open && setPayload(null)}
        size="lg"
        title="What would be sent"
        description="One request, with the whole document in it — instead of one request per line while you type. This is the payload Phase 5's POST /api/invoices has to accept, which is easier to argue about than a screen."
        footer={<Button onClick={() => setPayload(null)}>Close</Button>}
      >
        <pre className="max-h-96 overflow-auto rounded-lg bg-surface-sunken p-4 font-mono text-xs text-text">
          {payload}
        </pre>
      </Dialog>
    </div>
  );
}

/**
 * The measurement.
 *
 * Legacy: each line added is an `addtoCart` POST, each line removed a `removeQItem`, and each of
 * those is followed by a `cartLoad` to redraw the grid from the session — two round trips per line
 * operation (FUNCTIONS.md §4). The new editor sends nothing while typing, and one request at save.
 *
 * The counter is honest about being an estimate of the *old* number: it counts what this draft would
 * have cost over there, not what anybody has measured on a stopwatch. That is what the user's own
 * timing is for.
 */
function RoundTrips({ lineOperations }: { lineOperations: number }) {
  const legacy = lineOperations * 2;

  return (
    <Card>
      <h2 className="text-sm font-medium text-text">Server round trips for this draft</h2>

      <dl className="mt-4 grid grid-cols-2 gap-4">
        <div>
          <dt className="text-xs uppercase tracking-wide text-muted">Legacy (estimated)</dt>
          <dd className="mt-1 text-2xl font-semibold tabular text-danger">{legacy}</dd>
          <dd className="mt-1 text-xs text-muted">
            addtoCart / removeQItem, each followed by cartLoad
          </dd>
        </div>

        <div>
          <dt className="text-xs uppercase tracking-wide text-muted">This editor</dt>
          <dd className="mt-1 text-2xl font-semibold tabular text-success-text">0</dd>
          <dd className="mt-1 text-xs text-muted">
            plus one request when you press Save
          </dd>
        </div>
      </dl>

      <p className="mt-4 max-w-prose text-sm text-muted">
        Nothing is sent while you type, so nothing can be lost to a slow connection halfway through a
        line — and two tabs cannot share one cart, which today they do.
      </p>
    </Card>
  );
}

function Shortcuts({ kind }: { kind: DocumentKind }) {
  // The same contract on both paths — only what you type into differs. The person typing should not
  // have to learn two keyboards to raise two documents.
  const keys =
    kind === "item"
      ? [
          ["Type", "Search the item master by code or by words in the name"],
          ["↑ ↓", "Move through the matches"],
          ["Enter", "Add the highlighted item, and jump to its quantity"],
          ["Enter again", "Back to the search field, ready for the next line"],
          ["Tab", "Quantity → unit price → discount"],
          ["Esc", "Out of anything, back to the search field"],
        ]
      : [
          ["Type", "Whatever you are invoicing for, in your own words"],
          ["Enter", "Add the line, and jump straight to its price — the one thing only you know"],
          ["Enter again", "Back to the entry field, ready for the next line"],
          ["Tab", "Quantity → unit price → discount"],
          ["Esc", "Out of anything, back to the entry field"],
        ];

  return (
    <Card>
      <h2 className="flex items-center gap-2 text-sm font-medium text-text">
        <Keyboard className="size-4 text-muted" aria-hidden />
        The keyboard
      </h2>

      <dl className="mt-4 space-y-2 text-sm">
        {keys.map(([key, meaning]) => (
          <div key={meaning} className="flex gap-3">
            <dt className="w-24 shrink-0">
              <kbd className="rounded border border-subtle bg-surface-sunken px-1.5 py-0.5 font-mono text-xs text-muted">
                {key}
              </kbd>
            </dt>
            <dd className="text-muted">{meaning}</dd>
          </div>
        ))}
      </dl>
    </Card>
  );
}
