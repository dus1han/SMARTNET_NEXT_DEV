import { NextResponse, type NextRequest } from "next/server";

const AUTH_COOKIE = "smartnet_auth";
const PUBLIC_PATHS = ["/login"];

/**
 * Sends signed-out visitors to the login page.
 *
 * (This is `proxy.ts`, not `middleware.ts`: the middleware file convention is deprecated in
 * Next 16 and renamed to proxy.)
 *
 * IMPORTANT: this is a convenience, NOT a security control. It checks only that an auth cookie is
 * *present* — it does not verify the signature, the expiry, or a single permission, and it could
 * not: the signing key belongs on the server, and a guard that a user defeats by inventing a
 * cookie named smartnet_auth is worth nothing.
 *
 * The real enforcement is in the API, which denies by default and validates the token on every
 * request. That is the lesson of ISSUES A5 — the legacy app hid menu items from users who lacked
 * a permission while leaving the endpoints behind them wide open. Hiding a door is not locking
 * it. This file only spares a signed-out user the flash of an empty page.
 */
export function proxy(request: NextRequest) {
  const { pathname, search } = request.nextUrl;

  if (PUBLIC_PATHS.some((path) => pathname.startsWith(path))) {
    return NextResponse.next();
  }

  if (request.cookies.has(AUTH_COOKIE)) {
    return NextResponse.next();
  }

  // Carry the destination, so signing in resumes it instead of landing on the dashboard. This is the
  // arrive-with-no-cookie case — the cookie's lifetime matches the token's 30 minutes, so it is simply
  // what happens to anyone who comes back after lunch and clicks a bookmark. `lib/session.ts` does the
  // same for a session that dies while the tab is open; both read back in the login form.
  const login = new URL("/login", request.url);
  login.searchParams.set("next", `${pathname}${search}`);

  return NextResponse.redirect(login);
}

export const config = {
  // Everything except Next's own assets and the favicon.
  matcher: ["/((?!_next/static|_next/image|favicon.ico).*)"],
};
