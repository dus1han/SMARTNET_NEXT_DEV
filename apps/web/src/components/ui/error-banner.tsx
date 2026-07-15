import { AlertTriangle } from "lucide-react";
import { cn } from "@/lib/cn";

/**
 * How a failure is shown to a user, everywhere in the application.
 *
 * The correlation id is the whole point. The API deliberately never returns a stack trace — the
 * legacy app returned `ex.ToString()` straight to the browser (ISSUES A9) — so this reference is the
 * only thing connecting "it broke" to the log line that says why. The user reads it down the phone;
 * you grep for it. Without it, a generic error message is just a shrug.
 */
export function ErrorBanner({ message, correlationId, className }: {
  message: string;
  correlationId?: string;
  className?: string;
}) {
  return (
    <div
      role="alert"
      className={cn(
        "flex gap-3 rounded-md border border-danger/25 bg-danger-subtle px-3 py-2.5 text-sm text-danger",
        className,
      )}
    >
      <AlertTriangle className="mt-0.5 size-4 shrink-0" aria-hidden />

      <div className="min-w-0">
        <p>{message}</p>

        {correlationId && (
          <p className="mt-1 font-mono text-xs opacity-75">
            Reference: <span className="select-all">{correlationId}</span>
          </p>
        )}
      </div>
    </div>
  );
}
