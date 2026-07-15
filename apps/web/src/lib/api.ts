/**
 * The one way this app talks to the API.
 *
 * Every call sends the auth cookie and, on failure, raises an ApiError carrying the server's
 * correlation id — the string the user reads back to you when they phone. The API deliberately
 * never returns a stack trace (the legacy app returned `ex.ToString()` to the browser), so the
 * correlation id is the entire link between "it broke" and the log line that says why.
 */

/** The API origin. Exported so the file-download path (which cannot use `api()`) hits the same host. */
export const API_BASE_URL = process.env.NEXT_PUBLIC_API_URL ?? "http://localhost:5080";
const BASE_URL = API_BASE_URL;

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
  const { method = "GET", body, reason, companyId } = options;

  const headers: Record<string, string> = {};

  if (body !== undefined) {
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
    body: body === undefined ? undefined : JSON.stringify(body),

    // The auth token is an httpOnly cookie: JavaScript cannot read it, and it will not be sent
    // cross-origin unless we ask for it explicitly.
    credentials: "include",
  });

  if (response.ok) {
    // 204 No Content — logout and change-password both return one.
    return response.status === 204 ? (undefined as T) : ((await response.json()) as T);
  }

  const problem = await readProblem(response);

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
