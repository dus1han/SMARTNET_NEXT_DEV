import { cva, type VariantProps } from "class-variance-authority";
import type { HTMLAttributes, ReactNode } from "react";
import { cn } from "@/lib/cn";

export function Card({
  className,
  interactive,
  ...props
}: HTMLAttributes<HTMLDivElement> & {
  /** Adds a hover lift + border emphasis. For a card that is a link or opens something. */
  interactive?: boolean;
}) {
  return (
    <div
      className={cn(
        "rounded-lg border border-subtle bg-surface p-5 shadow-sm",
        interactive &&
          "transition duration-200 ease-out hover:-translate-y-0.5 hover:border-strong hover:shadow-md",
        className,
      )}
      {...props}
    />
  );
}

export function CardHeader({ title, description, actions }: {
  title: ReactNode;
  description?: ReactNode;
  actions?: ReactNode;
}) {
  return (
    <div className="mb-4 flex flex-wrap items-start justify-between gap-3">
      <div>
        <h2 className="font-medium text-text">{title}</h2>
        {description && <p className="mt-1 text-sm text-muted">{description}</p>}
      </div>
      {actions}
    </div>
  );
}

const badge = cva("inline-flex items-center rounded px-2 py-0.5 text-xs font-medium", {
  variants: {
    tone: {
      neutral: "bg-surface-sunken text-muted",
      danger: "bg-danger-subtle text-danger",
      warning: "bg-warning-subtle text-warning-text",
      success: "bg-success-subtle text-success-text",
    },
  },
  defaultVariants: { tone: "neutral" },
});

export function Badge({
  className,
  tone,
  ...props
}: HTMLAttributes<HTMLSpanElement> & VariantProps<typeof badge>) {
  return <span className={cn(badge({ tone }), className)} {...props} />;
}

/**
 * A loading placeholder shaped like the thing that is loading.
 *
 * Used instead of a spinner because a spinner tells the user nothing about what is coming, and the
 * layout jumps when it arrives. A skeleton reserves the space.
 */
export function Skeleton({ className, ...props }: HTMLAttributes<HTMLDivElement>) {
  return (
    <div
      className={cn("animate-pulse rounded-md bg-surface-sunken", className)}
      // The row is decorative; what matters is that assistive tech is told the region is busy.
      aria-hidden
      {...props}
    />
  );
}
