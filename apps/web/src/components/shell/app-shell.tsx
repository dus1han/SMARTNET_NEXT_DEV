"use client";

import { useQuery } from "@tanstack/react-query";
import { useEffect, useState, type ReactNode } from "react";
import { me } from "@/lib/auth";
import { endSession } from "@/lib/session";
import { cn } from "@/lib/cn";
import { Skeleton } from "@/components/ui";
import { Sidebar } from "./sidebar";
import { Topbar } from "./topbar";

/**
 * The frame every signed-in screen sits in.
 *
 * It no longer owns the session check. The `proxy.ts` guard only sees that *a* cookie exists — it
 * cannot verify a signature, and must not try, because the signing key belongs on the server — so an
 * expired or forged token is caught on the first real API call, in `api.ts`, for every request rather
 * than only this one. See `lib/session.ts`: doing it here alone is what left users on a drawn shell
 * where every action failed.
 *
 * What remains here is the one thing `api.ts` cannot see: a user whose session is perfectly valid but
 * who must change their password before using it.
 */
export function AppShell({ children }: { children: ReactNode }) {
  const [navOpen, setNavOpen] = useState(false);

  const { data: user, isPending } = useQuery({ queryKey: ["me"], queryFn: me });

  // `/api/auth/me` is deliberately allow-listed by MustChangePasswordMiddleware, so it answers 200
  // while every other endpoint answers 403. Without this the shell renders in full for such a user and
  // each thing they touch fails into a toast — the same "logged in but nothing works" state, reached a
  // different way. It bit anyone arriving at / directly, by bookmark or refresh, rather than through
  // the sign-in form.
  useEffect(() => {
    if (user?.mustChangePassword) {
      endSession("password_change_required");
    }
  }, [user]);

  if (isPending || !user || user.mustChangePassword) {
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
