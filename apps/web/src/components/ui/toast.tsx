"use client";

import { useTheme } from "next-themes";
import { Toaster as Sonner, toast } from "sonner";

/**
 * Transient confirmations — "User created", "Settings saved".
 *
 * Deliberately NOT how failures are reported. A toast disappears, and an error the user needed to
 * read (and read a correlation id out of) must not vanish while they are reaching for a pen. Errors
 * use ErrorBanner, which stays on screen. Toasts are for things that went right.
 */
export function Toaster() {
  const { resolvedTheme } = useTheme();

  return (
    <Sonner
      theme={resolvedTheme === "dark" ? "dark" : "light"}
      position="bottom-right"
      // The tokens, so a toast is not the one thing in the app that ignores the theme.
      toastOptions={{
        classNames: {
          toast: "bg-surface border-subtle text-text",
          description: "text-muted",
        },
      }}
    />
  );
}

export { toast };
