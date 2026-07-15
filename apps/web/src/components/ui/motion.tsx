"use client";

import { Loader2 } from "lucide-react";
import type { ReactNode } from "react";
import { cn } from "@/lib/cn";

/**
 * The motion primitives.
 *
 * House rules (DEVELOPMENT.md §8): 150–250ms, ease-out, and nothing animates while the user is
 * typing. Everything here is disabled outright under `prefers-reduced-motion` by the global rule in
 * globals.css — for some people motion causes nausea, and that is not a preference to negotiate with
 * per component.
 */

/** A spinner. Sized to whatever it sits in. */
export function Spinner({ className, label = "Loading" }: { className?: string; label?: string }) {
  return (
    <span role="status" aria-live="polite">
      <Loader2 className={cn("size-4 animate-spin text-muted", className)} aria-hidden />
      <span className="sr-only">{label}</span>
    </span>
  );
}

/** A full-panel loading state, for when there is nothing yet to show a skeleton of. */
export function LoadingPanel({ label = "Loading…" }: { label?: string }) {
  return (
    <div className="flex flex-col items-center justify-center gap-3 py-16">
      <Spinner className="size-6 text-primary" />
      <p className="text-sm text-muted">{label}</p>
    </div>
  );
}

/**
 * An indeterminate progress bar.
 *
 * Used when the wait has no measurable progress — a save, a page load. It is honest about that: it
 * slides rather than filling, so it never implies "63% done" when nothing knows that.
 */
export function ProgressBar({ className }: { className?: string }) {
  return (
    <div
      role="progressbar"
      aria-busy="true"
      aria-label="Loading"
      className={cn("h-0.5 w-full overflow-hidden bg-primary-ghost", className)}
    >
      <div className="h-full w-1/3 animate-indeterminate rounded-full bg-primary" />
    </div>
  );
}

/**
 * Fades and lifts its children in, staggered.
 *
 * The stagger is what makes a grid of cards feel like it arrived rather than blinked. Kept to 40ms
 * a step and capped: past about six items a stagger stops reading as choreography and starts reading
 * as lag.
 */
export function Stagger({ children, className }: { children: ReactNode[]; className?: string }) {
  return (
    <div className={className}>
      {children.map((child, index) => (
        <div
          key={index}
          className="animate-in fade-in-0 slide-in-from-bottom-2 fill-mode-backwards duration-300 ease-out"
          style={{ animationDelay: `${Math.min(index, 5) * 40}ms` }}
        >
          {child}
        </div>
      ))}
    </div>
  );
}

/** The standard entrance for a page's content. */
export function FadeIn({ children, className }: { children: ReactNode; className?: string }) {
  return (
    <div
      className={cn(
        "animate-in fade-in-0 slide-in-from-bottom-1 duration-300 ease-out",
        className,
      )}
    >
      {children}
    </div>
  );
}
