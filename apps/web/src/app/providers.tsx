"use client";

import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { ThemeProvider } from "next-themes";
import { useState, type ReactNode } from "react";
import { ApiError } from "@/lib/api";
import { RouteProgress, Toaster } from "@/components/ui";

export function Providers({ children }: { children: ReactNode }) {
  // Created once per browser session, in state rather than at module scope: a module-level client
  // would be shared across every request on the server and leak one user's cached data into
  // another's page.
  const [client] = useState(
    () =>
      new QueryClient({
        defaultOptions: {
          queries: {
            retry: (failureCount, error) => {
              // Retrying a 401 or a 403 is pointless — the answer will not change, and each retry
              // is another audited request. Retry only what might genuinely be transient.
              if (error instanceof ApiError && error.status >= 400 && error.status < 500) {
                return false;
              }

              return failureCount < 2;
            },

            staleTime: 30_000,
          },
        },
      }),
  );

  return (
    <ThemeProvider
      attribute="class"
      defaultTheme="system"
      // The user's choice must be able to overrule the OS — someone on a bright shop floor with a
      // dark-themed laptop still wants to read an invoice.
      enableSystem
      // next-themes writes the class before paint; without this the page renders light and then
      // snaps to dark, which is both ugly and, in a dark room, unpleasant.
      disableTransitionOnChange
    >
      <QueryClientProvider client={client}>
        {/* Inside the provider: it is driven by TanStack's in-flight counts, so it reports what the
            user is actually waiting for — the data — rather than what the router thinks. */}
        <RouteProgress />
        {children}
        <Toaster />
      </QueryClientProvider>
    </ThemeProvider>
  );
}
