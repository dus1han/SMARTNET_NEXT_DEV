/**
 * The one way this app talks to the API.
 *
 * Every call sends the auth cookie and, on failure, raises an ApiError carrying the server's
 * correlation id — the string the user reads back to you when they phone. The API deliberately
 * never returns a stack trace (the legacy app returned `ex.ToString()` to the browser), so the
 * correlation id is the entire link between "it broke" and the log line that says why.
 */

/**
 * The API origin — empty in a deployment, so every call is same-origin.
 *
 * **Relative on purpose.** Nginx routes /api to the API container and everything else to this app, so
 * the browser and the API share an origin and a bare "/api/..." resolves correctly with no
 * configuration at all. Three things follow from that, each of which bit the alternative:
 *
 *   - `NEXT_PUBLIC_*` is inlined at **build** time, so an absolute origin would bake one image per
 *     environment and the artefact tested in staging could never be the one that ships.
 *   - The old fallback was `http://localhost:5080`. In a user's browser that is *their* machine, so a
 *     misconfigured deployment failed by quietly calling the wrong computer.
 *   - The auth cookie is SameSite=Strict. Same-origin is unambiguously fine; a cross-origin absolute
 *     URL is one mistake away from the cookie not being sent and every call reading as logged out.
 *
 * Development is the genuine exception: `next dev` serves :3000 while the API runs on :5080, so they
 * really are different origins. Keyed on NODE_ENV, which Next replaces at build time, so the localhost
 * default cannot leak into a production bundle — and a fresh clone still runs with no env file.
 *
 * Exported because the download and preview paths cannot use `api()` and must hit the same host.
 */
export const API_BASE_URL =
  process.env.NEXT_PUBLIC_API_URL
  ?? (process.env.NODE_ENV === "development" ? "http://localhost:5080" : "");

const BASE_URL = API_BASE_URL;

import { endSession, isCredentialCheck } from "./session";

/** RFC 9457 ProblemDetails, plus the extensions our API adds. */
interface ProblemDetails {
  title?: string;
  status?: number;
  detail?: string;
  correlationId?: string;
  code?: string;
  errors?: Record<string, string[]>;
}

export class ApiError extends Error {
  constructor(
    readonly status: number,
    message: string,
    readonly correlationId?: string,
    /** A stable machine-readable code, e.g. "password_change_required". */
    readonly code?: string,
    /** Field-level validation messages, keyed by field name. */
    readonly fieldErrors?: Record<string, string[]>,
  ) {
    super(message);
    this.name = "ApiError";
  }
}

interface RequestOptions {
  method?: "GET" | "POST" | "PUT" | "DELETE";
  body?: unknown;
  /**
   * The X-Change-Reason header. Mandatory on the actions listed in AUDIT.md §5 — the server
   * rejects those requests without it, so this is not decoration.
   */
  reason?: string;
  /**
   * The company this one request acts in, overriding the ambient active company.
   *
   * Almost nothing sets this: normal screens act in whichever company the user is working in, and
   * that lives in localStorage. The Settings screen is the exception — it configures a *specific*
   * entity the administrator picks on the page, independent of any ambient choice, so it names the
   * company per request rather than mutating a global one.
   */
  companyId?: number;
  /**
   * Let this request outlive the page that started it.
   *
   * Only the draft autosave sets it. When a tab is closed or navigated away from, an ordinary fetch in
   * flight is cancelled — which is precisely the moment the last few seconds of typing most need to
   * reach the server. `keepalive` hands the request to the browser to finish on its own.
   *
   * Not a default: a keepalive request cannot be aborted, shares a small per-page byte budget, and its
   * response is of no use to a page that has gone. It is right for "save this and I don't care what you
   * say back", and wrong for everything else.
   */
  keepalive?: boolean;
}

const ACTIVE_COMPANY_KEY = "smartnet.activeCompany";

/**
 * The company the user is currently working in.
 *
 * Kept in localStorage because it is a UI preference, not a credential: the server decides which
 * companies the token permits, and simply ignores a header naming one it does not. So the worst a
 * tampered value can do is switch you to a company you were already allowed to see.
 */
export function getActiveCompany(): number | null {
  if (typeof window === "undefined") return null;

  const stored = window.localStorage.getItem(ACTIVE_COMPANY_KEY);
  const parsed = stored === null ? Number.NaN : Number(stored);

  return Number.isInteger(parsed) ? parsed : null;
}

export function setActiveCompany(companyId: number) {
  window.localStorage.setItem(ACTIVE_COMPANY_KEY, String(companyId));
}

export async function api<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const { method = "GET", body, reason, companyId, keepalive } = options;

  const headers: Record<string, string> = {};

  // FormData carries its own encoding. Setting Content-Type by hand would omit the multipart boundary
  // the browser generates, and the server would reject a body it cannot split into parts.
  const isFormData = typeof FormData !== "undefined" && body instanceof FormData;

  if (body !== undefined && !isFormData) {
    headers["Content-Type"] = "application/json";
  }

  if (reason) {
    headers["X-Change-Reason"] = reason;
  }

  // An explicit per-request company wins over the ambient one. The server still ignores a company
  // the caller's token does not permit, so this can only ever narrow to something already allowed.
  const company = companyId ?? getActiveCompany();

  if (company !== null) {
    headers["X-Company-Id"] = String(company);
  }

  const response = await fetch(`${BASE_URL}${path}`, {
    method,
    headers,
    body: body === undefined ? undefined : isFormData ? (body as FormData) : JSON.stringify(body),

    // The auth token is an httpOnly cookie: JavaScript cannot read it, and it will not be sent
    // cross-origin unless we ask for it explicitly.
    credentials: "include",

    keepalive,
  });

  if (response.ok) {
    // 204 No Content — logout and change-password both return one.
    return response.status === 204 ? (undefined as T) : ((await response.json()) as T);
  }

  const problem = await readProblem(response);

  // The session boundary, handled once, here — because this is the one place every call passes through.
  //
  // It used to live in AppShell, keyed on the ["me"] query alone, which meant a 401 on any of the other
  // sixty screens' queries became a toast and nothing more: the user sat on a fully-drawn app where
  // everything failed. See lib/session.ts for why that produced both reported faults.
  //
  // The sign-in request is excluded: its 401 means "wrong password", and the form has to be able to say
  // so rather than reloading itself.
  if (!isCredentialCheck(path)) {
    if (response.status === 401) {
      endSession("expired");
    } else if (response.status === 403 && problem.code === "password_change_required") {
      endSession("password_change_required");
    }
  }

  throw new ApiError(
    response.status,
    problem.title ?? `Request failed (${response.status}).`,
    problem.correlationId ?? response.headers.get("X-Correlation-Id") ?? undefined,
    problem.code,
    problem.errors,
  );
}

/** A failing response is not guaranteed to contain JSON — a proxy 502 certainly will not. */
async function readProblem(response: Response): Promise<ProblemDetails> {
  try {
    return (await response.json()) as ProblemDetails;
  } catch {
    return {};
  }
}
