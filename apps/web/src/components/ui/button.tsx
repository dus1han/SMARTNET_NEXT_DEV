"use client";

import { Slot } from "@radix-ui/react-slot";
import { cva, type VariantProps } from "class-variance-authority";
import { Loader2 } from "lucide-react";
import type { ButtonHTMLAttributes } from "react";
import { cn } from "@/lib/cn";

const button = cva(
  cn(
    "inline-flex items-center justify-center gap-2 whitespace-nowrap rounded-md text-sm font-medium",
    // 200ms, ease-out — and disabled globally under prefers-reduced-motion (globals.css).
    "transition-colors duration-200 ease-out",
    // Focus must be visible for anyone driving this by keyboard, which in a data-entry app that
    // people use all day is most of them.
    "outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 focus-visible:ring-offset-canvas",
    "disabled:pointer-events-none disabled:opacity-50",
    "[&_svg]:size-4 [&_svg]:shrink-0",
  ),
  {
    variants: {
      variant: {
        // A soft shadow tinted with the accent, so the primary action lifts off the page rather
        // than sitting flat on it.
        primary:
          "bg-primary text-primary-text shadow-sm shadow-primary/25 hover:bg-primary-hover hover:shadow-md hover:shadow-primary/25",
        secondary: "border border-subtle bg-surface text-text shadow-xs hover:bg-surface-sunken hover:border-strong",
        ghost: "text-muted hover:bg-surface-sunken hover:text-text",
        // Destructive actions look destructive. In the legacy app "Delete Invoice" is the same grey
        // button as "Save", sitting next to it.
        danger: "bg-danger text-danger-text shadow-sm shadow-danger/25 hover:opacity-90",
      },
      size: {
        sm: "h-8 px-3",
        md: "h-9 px-4",
        lg: "h-10 px-5",
        icon: "size-9",
      },
    },
    defaultVariants: { variant: "primary", size: "md" },
  },
);

export interface ButtonProps
  extends ButtonHTMLAttributes<HTMLButtonElement>,
    VariantProps<typeof button> {
  /** Renders the child element instead of a <button> — for a link that looks like a button. */
  asChild?: boolean;

  /**
   * Shows a spinner and disables the button.
   *
   * Not cosmetic: without it, a slow save invites a second click, and a double-clicked Save is the
   * exact bug that produced Rs. 1.55M of duplicate payments in the legacy system.
   */
  pending?: boolean;
}

export function Button({
  className,
  variant,
  size,
  asChild,
  pending,
  disabled,
  children,
  ...props
}: ButtonProps) {
  const Component = asChild ? Slot : "button";

  return (
    <Component
      className={cn(button({ variant, size }), className)}
      disabled={disabled || pending}
      {...props}
    >
      {pending && <Loader2 className="animate-spin" aria-hidden />}
      {children}
    </Component>
  );
}
