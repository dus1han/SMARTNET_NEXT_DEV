"use client";

import { ArrowRight, KeyRound, ScrollText, ShieldCheck, type LucideIcon } from "lucide-react";
import { useRouter } from "next/navigation";
import { useState, type FormEvent } from "react";
import { ApiError } from "@/lib/api";
import { login } from "@/lib/auth";
import { cn } from "@/lib/cn";
import { BRAND_NAME, BRAND_TAGLINE, BrandMark } from "@/components/shell/brand";
import { Button, ErrorBanner, Input, PasswordInput } from "@/components/ui";

/**
 * The sign-in screen — the one screen allowed to be inviting rather than merely efficient.
 *
 * It is the first thing a new member of staff sees, and the only screen in the application with
 * nothing on it to read. Everywhere past this door the motion is functional and short (150–250ms)
 * because the user came to get something done; here it can take its time.
 *
 * Two house rules are still enforced, not waived:
 *
 *   - **Nothing animates while you type.** The drifting light behind the panel is paused the moment
 *     anything on the page takes focus — `group-focus-within` below. A background that keeps moving
 *     under a password field is exactly what that rule exists to forbid.
 *   - **`prefers-reduced-motion` kills all of it**, by the global rule in globals.css. For some
 *     people motion causes nausea, and that is not a preference to negotiate with.
 */
export default function LoginPage() {
  const router = useRouter();

  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [pending, setPending] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    setPending(true);
    setError(null);

    try {
      const result = await login(username, password);

      // The server enforces this too — it refuses every other endpoint until the password is
      // changed — so this redirect is a convenience, not the control.
      router.push(result.mustChangePassword ? "/change-password" : "/");
      router.refresh();
    } catch (caught) {
      setError(
        caught instanceof ApiError
          ? caught
          : new ApiError(0, "Could not reach the server. Check your connection."),
      );
    } finally {
      setPending(false);
    }
  }

  return (
    <div className="group grid min-h-dvh lg:grid-cols-[1.05fr_1fr]">
      <BrandPanel />

      <main className="relative flex items-center justify-center overflow-hidden p-6">
        {/* A single wash of brand colour behind the form. On a phone, where the panel is hidden,
            this is the only thing keeping the screen from being a white page with two boxes. */}
        <div
          aria-hidden
          className={cn(
            "pointer-events-none absolute -top-40 left-1/2 size-[36rem] -translate-x-1/2 rounded-full",
            "bg-primary/10 blur-3xl animate-drift-alt",
            "group-focus-within:[animation-play-state:paused]",
          )}
        />

        <div className="relative w-full max-w-sm">
          {/* The logo again, for the phone, where the panel is hidden. */}
          <div className="mb-8 flex items-center gap-3 lg:hidden">
            <BrandMark className="size-10" />
            <p className="font-semibold text-text">{BRAND_NAME}</p>
          </div>

          <div className="animate-in fade-in-0 slide-in-from-bottom-3 duration-500 ease-out">
            <h1 className="text-2xl font-semibold tracking-tight text-text">Welcome back</h1>
            <p className="mt-1 text-sm text-muted">Sign in to pick up where you left off.</p>
          </div>

          <form
            onSubmit={onSubmit}
            className={cn(
              "mt-8 space-y-4 rounded-2xl border border-subtle bg-surface p-6 shadow-xl shadow-primary/5",
              // Lifts and settles on arrival, and the border warms when the user starts typing —
              // 200ms, which is the difference between "responsive" and "showing off".
              "animate-in fade-in-0 slide-in-from-bottom-4 fill-mode-backwards duration-500 ease-out [animation-delay:120ms]",
              "transition-colors duration-200 ease-out focus-within:border-strong",
            )}
          >
            {error && <ErrorBanner message={error.message} correlationId={error.correlationId} />}

            <Input
              label="Username"
              name="username"
              autoComplete="username"
              autoFocus
              required
              value={username}
              onChange={(e) => setUsername(e.target.value)}
            />

            <PasswordInput
              label="Password"
              name="password"
              autoComplete="current-password"
              required
              value={password}
              onChange={(e) => setPassword(e.target.value)}
            />

            {/* pending disables the button as well as spinning it. A double-clicked submit is not a
                cosmetic problem — it is the bug that produced Rs. 1.55M of duplicate payments. */}
            <Button
              type="submit"
              pending={pending}
              size="lg"
              className="group/button relative w-full overflow-hidden"
            >
              Sign in
              <ArrowRight className="transition-transform duration-200 ease-out group-hover/button:translate-x-0.5" />

              {/* One pass of light across the button on hover. It runs once and stops — a control
                  that shimmers forever is a control that is asking to be looked at rather than
                  pressed. */}
              <span
                aria-hidden
                className={cn(
                  "pointer-events-none absolute inset-y-0 -left-1/3 w-1/3",
                  "bg-linear-to-r from-transparent via-white/25 to-transparent",
                  "opacity-0 group-hover/button:opacity-100 group-hover/button:animate-sheen",
                )}
              />
            </Button>
          </form>

          <p className="mt-6 text-center text-xs text-muted animate-in fade-in-0 duration-700 [animation-delay:300ms] fill-mode-backwards">
            Forgotten your password? An administrator can issue you a new one — it is single-use, and
            you choose your own the moment you sign in.
          </p>
        </div>
      </main>
    </div>
  );
}

/**
 * The half of the screen that has to earn the other half's trust.
 *
 * What it says is chosen on purpose. The people signing in are the same people who lived with the
 * old system, and the three claims below are the three things it could not do: it left its admin
 * endpoints open, it stored passwords in clear text, and it recorded nothing about who changed what.
 */
function BrandPanel() {
  return (
    <aside className="relative hidden overflow-hidden bg-sidebar p-12 lg:flex lg:flex-col lg:justify-between">
      {/* The aurora. Two slow orbs, out of phase, so the light never repeats a shape you can catch
          it repeating — and both paused the instant a field takes focus. */}
      <div
        aria-hidden
        className="pointer-events-none absolute -left-32 -top-32 size-[30rem] rounded-full bg-primary/30 blur-3xl animate-drift group-focus-within:[animation-play-state:paused]"
      />
      <div
        aria-hidden
        className="pointer-events-none absolute -bottom-40 -right-24 size-[34rem] rounded-full bg-primary/20 blur-3xl animate-drift-alt group-focus-within:[animation-play-state:paused]"
      />

      {/* A faint grid, faded out towards the edges — structure under the light, which is roughly the
          claim the whole product is making. */}
      <div
        aria-hidden
        className="pointer-events-none absolute inset-0 opacity-[0.07]"
        style={{
          backgroundImage:
            "linear-gradient(to right, white 1px, transparent 1px), linear-gradient(to bottom, white 1px, transparent 1px)",
          backgroundSize: "48px 48px",
          maskImage: "radial-gradient(ellipse 80% 60% at 50% 40%, black, transparent)",
          WebkitMaskImage: "radial-gradient(ellipse 80% 60% at 50% 40%, black, transparent)",
        }}
      />

      <div className="relative flex items-center gap-3 animate-in fade-in-0 slide-in-from-left-2 duration-500 ease-out">
        <BrandMark className="size-10 rounded-xl shadow-lg" />
        <div className="leading-tight">
          <p className="font-semibold text-sidebar-text-active">{BRAND_NAME}</p>
          <p className="text-xs text-sidebar-text">{BRAND_TAGLINE}</p>
        </div>
      </div>

      <div className="relative max-w-md">
        <h2 className="text-4xl font-semibold leading-[1.15] tracking-tight text-sidebar-text-active animate-in fade-in-0 slide-in-from-bottom-3 fill-mode-backwards duration-700 ease-out [animation-delay:100ms]">
          Rebuilt on a foundation
          <br />
          you can audit.
        </h2>

        <p className="mt-4 max-w-sm text-sm leading-relaxed text-sidebar-text animate-in fade-in-0 fill-mode-backwards duration-700 [animation-delay:250ms]">
          The same business, the same numbers — and a system that can finally tell you who changed
          them.
        </p>

        <ul className="mt-10 space-y-6">
          {HIGHLIGHTS.map((highlight, index) => (
            <Highlight key={highlight.title} {...highlight} index={index} />
          ))}
        </ul>
      </div>

      <p className="relative flex items-center gap-2 text-xs text-sidebar-text/60">
        <span className="relative flex size-1.5">
          <span className="absolute inline-flex size-full animate-ping rounded-full bg-success opacity-60 group-focus-within:[animation-play-state:paused]" />
          <span className="relative inline-flex size-1.5 rounded-full bg-success" />
        </span>
        Running against the development database.
      </p>
    </aside>
  );
}

const HIGHLIGHTS = [
  {
    icon: ShieldCheck,
    title: "Deny by default",
    detail: "Every endpoint states who may call it, and the server checks — not the menu.",
  },
  {
    icon: KeyRound,
    title: "Argon2id passwords",
    detail: "Hashed and salted, upgraded silently the first time you sign in.",
  },
  {
    icon: ScrollText,
    title: "Everything audited",
    detail: "Who changed what, when, and why — recorded in the same transaction.",
  },
] satisfies { icon: LucideIcon; title: string; detail: string }[];

function Highlight({ icon: Icon, title, detail, index }: {
  icon: LucideIcon;
  title: string;
  detail: string;
  index: number;
}) {
  return (
    <li
      className="flex gap-4 animate-in fade-in-0 slide-in-from-bottom-2 fill-mode-backwards duration-500 ease-out"
      // Staggered by 90ms. Enough to read as choreography; past about six items it would read as lag.
      style={{ animationDelay: `${350 + index * 90}ms` }}
    >
      <span
        className={cn(
          "grid size-10 shrink-0 place-items-center rounded-xl bg-sidebar-active text-primary",
          "ring-1 ring-white/5",
        )}
      >
        <Icon className="size-4" aria-hidden />
      </span>

      <div>
        <p className="text-sm font-medium text-sidebar-text-active">{title}</p>
        <p className="mt-1 text-sm leading-relaxed text-sidebar-text">{detail}</p>
      </div>
    </li>
  );
}
