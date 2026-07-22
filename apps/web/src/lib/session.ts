/**
 * What happens when the server says you are no longer signed in.
 *
 * A session lasts an hour and now renews while it is being used, so ordinary work no longer walks into
 * the wall. It still ends: idle for an hour, or twelve hours after signing in however busy, and
 * `ClockSkew` is zero on the server. So expiry mid-task remains normal rather than exceptional — rarer,
 * but still something the app must have an answer for. It did not — this module is that answer.
 *
 * ## What was wrong
 *
 * Expiry was detected in exactly one place: the `["me"]` query in `AppShell`, whose error triggered a
 * redirect. That produced both of the reported faults.
 *
 * **"Auto-logout but I'm still on the page, with errors."** `["me"]` has a 30-second `staleTime` and no
 * polling, and `AppShell` mounts once and stays mounted for the whole session — moving between screens
 * does not remount it. So when the token died, every *other* query on the page started failing into
 * toasts and error banners while `["me"]` sat happily on cached data and never re-ran. The shell looked
 * fine; nothing worked. Only `refetchOnWindowFocus` eventually rescued it, which is precisely why it
 * happened *sometimes* — it needed the user to tab away and come back.
 *
 * **"I log in and instantly come back to the login screen."** The `QueryClient` lives above the route
 * groups, so a client-side `router.push("/login")` kept it — including the `["me"]` entry *in its error
 * state*, which the 4xx `retry: false` rule preserved verbatim. Signing in navigated back with that
 * cache intact, `AppShell` remounted, TanStack handed it the stale 401 synchronously on the first
 * render, and the effect bounced the user straight back out — with a valid, fresh cookie sitting in the
 * browser.
 *
 * ## What this does instead
 *
 * A 401 on *any* request ends the session, once, from `api.ts` — the single boundary every call already
 * passes through. And it leaves via a **full document navigation**, not `router.push`. That is the part
 * that matters: a hard navigation throws away the React tree and the `QueryClient` with it, so no stale
 * error can outlive the session that produced it. The second bug is not so much fixed as made
 * unrepresentable.
 */

/** Where the user was, so signing in can put them back rather than dumping them on the dashboard. */
const NEXT_PARAM = "next";

/**
 * Requests whose 401 means "those credentials are wrong", not "your session ended".
 *
 * Without this the sign-in form would be unusable: a mistyped password returns 401, which would end the
 * "session" and reload the page, and the user would never see the message telling them what happened.
 */
const CREDENTIAL_PATHS = ["/api/auth/login"];

export const isCredentialCheck = (path: string) =>
  CREDENTIAL_PATHS.some((candidate) => path.startsWith(candidate));

/**
 * One navigation per expiry, not one per failed request.
 *
 * A screen with six queries on it produces six 401s within a few milliseconds of each other. Without
 * this latch each would start its own navigation.
 */
let ending = false;

/** Pages that ARE the signed-out experience. Sending them to the sign-in screen is a loop. */
const isSignedOutPage = (pathname: string) =>
  pathname.startsWith("/login") || pathname.startsWith("/change-password");

/**
 * End the session and go where the user needs to be.
 *
 * @param reason `expired` for a 401; `password_change_required` for the 403 the server raises until a
 * forced password change is done — a different destination, and not a sign-out at all.
 */
export function endSession(reason: "expired" | "password_change_required") {
  if (typeof window === "undefined" || ending) return;

  const { pathname, search } = window.location;

  if (isSignedOutPage(pathname)) return;

  ending = true;

  if (reason === "password_change_required") {
    window.location.assign("/change-password");
    return;
  }

  // Carried so the user resumes where they were rather than at the dashboard. Only the path — never a
  // full URL, which would let a crafted link bounce someone off this origin after signing in.
  const next = encodeURIComponent(`${pathname}${search}`);

  window.location.assign(`/login?expired=1&${NEXT_PARAM}=${next}`);
}

/**
 * The path to return to after signing in — only ever a path on this origin.
 *
 * Anything absolute, protocol-relative (`//evil.test`), or otherwise not starting with a single `/` is
 * discarded in favour of the dashboard. An open redirect on a login form is worth more to an attacker
 * than it looks: it makes a phishing link end up on the real site, having passed through ours.
 */
export function safeReturnPath(raw: string | null): string {
  if (!raw) return "/";

  const decoded = (() => {
    try {
      return decodeURIComponent(raw);
    } catch {
      return "";
    }
  })();

  if (!decoded.startsWith("/") || decoded.startsWith("//")) return "/";

  return decoded;
}
