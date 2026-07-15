"use client";

import * as Primitive from "@radix-ui/react-dialog";
import { X } from "lucide-react";
import type { ReactNode } from "react";
import { cn } from "@/lib/cn";

/**
 * A modal dialog.
 *
 * Radix rather than a hand-rolled `<div>` with a fixed position, because a modal has to trap focus,
 * restore it on close, close on Escape, mark the page behind it inert, and label itself for a screen
 * reader. Every one of those is easy to get wrong and invisible when you do.
 */
export function Dialog({ open, onOpenChange, title, description, children, footer, size = "md" }: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  title: string;
  description?: ReactNode;
  children?: ReactNode;
  footer?: ReactNode;

  /**
   * A form fits in `md`. Content that is *read* rather than filled in — a history, a diff — needs
   * the room, and a side-by-side diff squeezed into 32rem is a diff nobody can see.
   */
  size?: "md" | "lg";
}) {
  return (
    <Primitive.Root open={open} onOpenChange={onOpenChange}>
      <Primitive.Portal>
        <Primitive.Overlay
          className={cn(
            "fixed inset-0 z-50 bg-black/40",
            "data-[state=open]:animate-in data-[state=open]:fade-in-0",
            "data-[state=closed]:animate-out data-[state=closed]:fade-out-0",
          )}
        />

        <Primitive.Content
          className={cn(
            "fixed left-1/2 top-1/2 z-50 w-[calc(100vw-2rem)] -translate-x-1/2 -translate-y-1/2",
            size === "lg" ? "max-w-3xl" : "max-w-lg",
            "rounded-lg border border-subtle bg-surface p-5 shadow-lg",
            // Long content scrolls inside the dialog rather than off the bottom of the viewport.
            "max-h-[calc(100dvh-4rem)] overflow-y-auto",
            "duration-200 ease-out",
            "data-[state=open]:animate-in data-[state=open]:fade-in-0 data-[state=open]:zoom-in-95",
            "data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=closed]:zoom-out-95",
          )}
        >
          <div className="mb-4 pr-8">
            <Primitive.Title className="font-medium text-text">{title}</Primitive.Title>

            {description && (
              <Primitive.Description className="mt-1 text-sm text-muted">
                {description}
              </Primitive.Description>
            )}
          </div>

          <Primitive.Close
            className={cn(
              "absolute right-4 top-4 rounded p-1 text-muted",
              "transition-colors duration-200 hover:bg-surface-sunken hover:text-text",
              "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring/25",
            )}
          >
            <X className="size-4" />
            <span className="sr-only">Close</span>
          </Primitive.Close>

          {children}

          {footer && <div className="mt-5 flex justify-end gap-2">{footer}</div>}
        </Primitive.Content>
      </Primitive.Portal>
    </Primitive.Root>
  );
}
