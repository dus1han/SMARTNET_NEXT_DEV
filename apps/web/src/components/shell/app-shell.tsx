"use client";

import { useQuery } from "@tanstack/react-query";
import { useRouter } from "next/navigation";
import { useEffect, useState, type ReactNode } from "react";
import { ApiError } from "@/lib/api";
import { me } from "@/lib/auth";
import { cn } from "@/lib/cn";
import { Skeleton } from "@/components/ui";
import { Sidebar } from "./sidebar";
import { Topbar } from "./topbar";

/**
 * The frame every signed-in screen sits in.
 *
 * It also owns the session check. The `proxy.ts` guard only sees that *a* cookie exists — it cannot
 * verify a signature, and it must not try, because the signing key belongs on the server. So an
 * expired or forged token is caught here, on the first real API call.
 */
export function AppShell({ children }: { children: ReactNode }) {
  const router = useRouter();
  const [navOpen, setNavOpen] = useState(false);

  const { data: user, error, isPending } = useQuery({ queryKey: ["me"], queryFn: me });

  useEffect(() => {
    if (!(error instanceof ApiError)) return;

    if (error.status === 401 || error.status === 403) {
      router.push(error.code === "password_change_required" ? "/change-password" : "/login");
    }
  }, [error, router]);

  if (isPending || !user) {
    return <ShellSkeleton />;
  }

  return (
    <div className="app-canvas flex h-dvh overflow-hidden">
      {/* Desktop: always there. Mobile: a drawer, because 256px of chrome on a phone leaves no
          room for the invoice. */}
      <div className="hidden lg:flex">
        <Sidebar permissions={user.permissions} />
      </div>

      {navOpen && (
        <div className="fixed inset-0 z-50 flex lg:hidden">
          <button
            type="button"
            aria-label="Close navigation"
            onClick={() => setNavOpen(false)}
            className="absolute inset-0 bg-black/50 animate-in fade-in-0"
          />
          <div className="relative animate-in slide-in-from-left-4 duration-200">
            <Sidebar permissions={user.permissions} onNavigate={() => setNavOpen(false)} />
          </div>
        </div>
      )}

      <div className="flex min-w-0 flex-1 flex-col">
        <Topbar user={user} onOpenNav={() => setNavOpen(true)} />

        <main className="flex-1 overflow-y-auto">
          <div className="mx-auto max-w-6xl p-4 sm:p-6 lg:p-8">{children}</div>
        </main>
      </div>
    </div>
  );
}

/**
 * The shell, in outline, while the session is checked.
 *
 * A skeleton rather than a spinner: it reserves the space the real thing will take, so the layout
 * does not jump when it arrives.
 */
function ShellSkeleton() {
  return (
    <div className="app-canvas flex h-dvh overflow-hidden">
      <div className="hidden w-64 shrink-0 border-r border-sidebar-border bg-sidebar lg:block" />

      <div className="flex flex-1 flex-col">
        <div className="h-16 border-b border-subtle bg-surface" />

        <div className="mx-auto w-full max-w-6xl space-y-4 p-8">
          <Skeleton className="h-8 w-56" />
          <div className="grid gap-4 sm:grid-cols-3">
            {[0, 1, 2].map((i) => (
              <Skeleton key={i} className="h-28" />
            ))}
          </div>
          <Skeleton className="h-64" />
        </div>
      </div>
    </div>
  );
}

/** The heading block every screen opens with. One place, so they all line up. */
export function PageHeader({ title, description, actions, className }: {
  title: string;
  description?: ReactNode;
  actions?: ReactNode;
  className?: string;
}) {
  return (
    <div className={cn("mb-6 flex flex-wrap items-start justify-between gap-4", className)}>
      <div>
        <h1 className="text-2xl font-semibold tracking-tight text-text">{title}</h1>
        {description && <p className="mt-1 text-sm text-muted">{description}</p>}
      </div>

      {actions && <div className="flex items-center gap-2">{actions}</div>}
    </div>
  );
}
