"use client";

import { useQueryClient } from "@tanstack/react-query";
import { ArrowRight } from "lucide-react";
import { useRouter, useSearchParams } from "next/navigation";
import { Suspense, useState, type FormEvent } from "react";
import { ApiError } from "@/lib/api";
import { login } from "@/lib/auth";
import { safeReturnPath } from "@/lib/session";
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
/**
 * `useSearchParams` forces the client tree up to the nearest Suspense boundary to be client-rendered,
 * so the boundary is here rather than swallowing the whole route. The brand panel is the slow half of
 * this screen and it does not depend on the query string, so it still prerenders and is in the initial
 * HTML — the sign-in card is the only thing that waits.
 */
export default function LoginPage() {
  return (
    <Suspense fallback={<LoginLayout />}>
      <LoginForm />
    </Suspense>
  );
}

function LoginForm() {
  const router = useRouter();
  const params = useSearchParams();
  const queryClient = useQueryClient();

  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [pending, setPending] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  // Set by endSession when a session runs out mid-task. Without it the user arrives here with no idea
  // why — they were half-way through an invoice a moment ago, and being silently returned to a sign-in
  // screen reads as the app having lost their work.
  const expired = params.get("expired") === "1";

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    setPending(true);
    setError(null);

    try {
      const result = await login(username, password);

      // Nothing from the previous session may outlive it. Expiry normally arrives here by a full
      // document navigation, which discards the cache anyway — but any other route to this screen
      // (a signed-out user following a link, a client-side push from elsewhere) keeps the QueryClient,
      // and a cached ["me"] error is exactly what used to bounce a freshly signed-in user straight
      // back out. Cheap, and it removes the whole class.
      queryClient.clear();

      // The server enforces this too — it refuses every other endpoint until the password is
      // changed — so this redirect is a convenience, not the control.
      router.push(result.mustChangePassword ? "/change-password" : safeReturnPath(params.get("next")));
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
    <div className="group grid min-h-dvh overflow-y-auto lg:grid-cols-[1.05fr_1fr]">
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
            {/* Only until they try again — a failed attempt's own message replaces it, rather than the
                two stacking up and contradicting each other. */}
            {expired && !error && (
              <p
                role="status"
                className="rounded-lg border border-subtle bg-surface px-3 py-2 text-sm text-muted"
              >
                Your session timed out. Sign in and we&rsquo;ll take you back to what you were doing.
              </p>
            )}

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
 * What is on screen for the instant before the form hydrates — the same frame and the same brand panel,
 * so the card fills in rather than the page rearranging itself around it.
 */
function LoginLayout() {
  return (
    <div className="group grid min-h-dvh overflow-y-auto lg:grid-cols-[1.05fr_1fr]">
      <BrandPanel />
      <main className="relative flex items-center justify-center overflow-hidden p-6">
        <div className="relative w-full max-w-sm">
          <div className="h-88 rounded-2xl border border-subtle bg-surface shadow-xl shadow-primary/5" />
        </div>
      </main>
    </div>
  );
}

/**
 * The half of the screen that has to earn the other half's trust.
 *
 * It says almost nothing, deliberately. It used to carry a headline, a paragraph and three claims
 * about the rebuild — true, and read exactly once. This screen is opened every morning by people who
 * already know what the system is, so what is left is the mark, the name and the light behind them.
 */
function BrandPanel() {
  return (
    <aside className="relative hidden overflow-hidden bg-sidebar p-12 lg:flex lg:items-center lg:justify-center">
      {/* The aurora. Two slow orbs, out of phase, so the light never repeats a shape you can catch
          it repeating.

          Nothing in this panel pauses on focus, and that is a correction rather than an exception:
          the form autofocuses its first field, so group-focus-within was true from the first paint
          and every animation here was frozen before anyone saw it move. The rule it came from —
          nothing animates while you type — is about motion beside the field you are typing in. This
          is the other half of the screen. The wash behind the form still pauses, because that one
          genuinely is beside the input. */}
      <div
        aria-hidden
        className="pointer-events-none absolute -left-32 -top-32 size-[30rem] rounded-full bg-primary/30 blur-3xl animate-drift"
      />
      <div
        aria-hidden
        className="pointer-events-none absolute -bottom-40 -right-24 size-[34rem] rounded-full bg-primary/20 blur-3xl animate-drift-alt"
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

      {/*
        The panel is the mark and the name, centred, and nothing else. It used to carry a headline, a
        paragraph and three feature bullets — copy nobody reads twice, on a screen its audience sees
        every morning. What survives is the thing that identifies the product.
      */}
      {/*
        The whole lockup drifts, not just the mark, so the name and tagline are moving too — the
        motion is the screen's resting state rather than something that happens once on arrival and
        stops. Mark and text share one animation so their spacing never breathes apart.
      */}
      <div className="relative flex flex-col items-center text-center animate-float">
        <div className="relative flex size-28 items-center justify-center">
          {/* Halo and ripple sit behind the mark and are decorative, so they are hidden from a
              screen reader and stop the moment a field takes focus — motion beside a form you are
              filling in is a distraction, not a flourish. */}
          <div
            aria-hidden
            className="absolute size-28 rounded-[2rem] bg-primary/40 blur-2xl animate-halo"
          />
          <div
            aria-hidden
            className="absolute size-24 rounded-[1.75rem] border border-primary/30 animate-ripple"
          />

          <BrandMark
            className="relative size-20 rounded-[1.5rem] shadow-2xl shadow-primary/30"
          />
        </div>

        <h1 className="mt-3 text-4xl font-semibold tracking-tight text-sidebar-text-active animate-in fade-in-0 slide-in-from-bottom-3 fill-mode-backwards duration-700 ease-out [animation-delay:120ms]">
          {BRAND_NAME}
        </h1>

        <p className="mt-1.5 text-sm tracking-wide text-sidebar-text animate-in fade-in-0 fill-mode-backwards duration-700 [animation-delay:280ms]">
          {BRAND_TAGLINE}
        </p>
      </div>
    </aside>
  );
}
