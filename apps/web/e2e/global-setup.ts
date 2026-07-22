import { spawn, type ChildProcess } from "node:child_process";
import { setTimeout as sleep } from "node:timers/promises";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { API_URL, WEB_URL } from "./seed";

const ROOT = path.resolve(__dirname, "../../..");
const STATE = path.join(__dirname, ".e2e-state.json");

const log = (m: string) => console.log(`[e2e-setup] ${m}`);
const pipe = (proc: ChildProcess, tag: string) => {
  proc.stdout?.on("data", (d) => process.stdout.write(String(d).replace(/^/gm, `[${tag}] `)));
  proc.stderr?.on("data", (d) => process.stderr.write(String(d).replace(/^/gm, `[${tag}] `)));
};

async function waitForHttp(url: string, label: string, tries = 150): Promise<void> {
  for (let i = 0; i < tries; i++) {
    try {
      const r = await fetch(url);
      if (r.ok || r.status === 401 || r.status === 404) return; // any HTTP answer means it is listening
    } catch {
      /* not up yet */
    }
    await sleep(1000);
  }
  throw new Error(`${label} did not become ready at ${url}`);
}

/** Start the throwaway-DB host and resolve once it prints its connection string. Its stdin is kept
 *  open so it blocks (holding the container) until teardown kills it. */
function startHost(): Promise<{ proc: ChildProcess; conn: string }> {
  log("starting E2E database host (throwaway MariaDB + seed)…");
  // --no-build is deliberate — building here would race the API spawn below. It does mean the host
  // must already be built, and a STALE one is the worst failure this harness has: it applies an older
  // migration set, so the schema silently lags the code and every save 500s with a missing column.
  // That is what `npm run e2e` builds first (see e2e:build in package.json) — do not run
  // `playwright test` directly unless you have just built both.
  const proc = spawn("dotnet", ["run", "--project", path.join(ROOT, "tools/E2EHost"), "--no-build"], {
    cwd: ROOT,
    stdio: ["pipe", "pipe", "pipe"],
  });
  let out = "";
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => reject(new Error(`E2EHost timed out.\n${out}`)), 180_000);
    proc.stdout!.on("data", (d) => {
      const s = String(d);
      out += s;
      process.stdout.write(s.replace(/^/gm, "[e2ehost] "));
      const m = out.match(/E2E_CONN=(.+)/);
      if (m && out.includes("E2E_READY")) {
        clearTimeout(timer);
        resolve({ proc, conn: m[1].trim() });
      }
    });
    proc.stderr!.on("data", (d) => {
      out += String(d);
      process.stderr.write(String(d).replace(/^/gm, "[e2ehost] "));
    });
    proc.on("exit", (c) => reject(new Error(`E2EHost exited early (code ${c}).\n${out}`)));
  });
}

function startApi(conn: string): ChildProcess {
  const exe = path.join(ROOT, "apps/api/Smartnet.Api/bin/Debug/net10.0/Smartnet.Api.exe");
  log(`starting API on ${API_URL} against the throwaway DB…`);

  // The data-protection key ring. Program.cs refuses to start without one — deliberately, so that a
  // deployment cannot put it somewhere a redeploy destroys and take every stored password with it.
  // That check was added after this harness was written, so the whole suite had stopped starting: the
  // API exited immediately and every spec failed at "API did not become ready".
  //
  // A fresh directory per run, thrown away with the run. Nothing it protects outlives the throwaway
  // database it is protecting things in.
  const keys = fs.mkdtempSync(path.join(os.tmpdir(), "smartnet-e2e-dpkeys-"));

  const proc = spawn(exe, [], {
    cwd: path.dirname(exe),
    stdio: ["ignore", "pipe", "pipe"],
    env: {
      ...process.env,
      ConnectionStrings__Smartnet: conn,
      ASPNETCORE_ENVIRONMENT: "Development",
      ASPNETCORE_URLS: API_URL,
      Jwt__SigningKey: "e2e-only-signing-key-0123456789-abcdefghij-klmnopqrst",
      Jwt__Issuer: "smartnet",
      Jwt__Audience: "smartnet",
      Cors__WebOrigin: WEB_URL,
      DataProtection__KeyPath: keys,
    },
  });
  pipe(proc, "api");
  return proc;
}

function startWeb(): ChildProcess {
  const port = new URL(WEB_URL).port || "3100";
  log(`starting web (next dev) on ${WEB_URL}…`);
  // shell:true is required on Windows — Node refuses to spawn npm.cmd directly (CVE-2024-27980).
  const proc = spawn("npm", ["run", "dev", "--", "-p", port], {
    cwd: path.join(ROOT, "apps/web"),
    stdio: ["ignore", "pipe", "pipe"],
    shell: true,
    // Its own build dir, so it never fights the .next of a dev server already running on another port.
    env: { ...process.env, NEXT_PUBLIC_API_URL: API_URL, NEXT_DIST_DIR: ".next-e2e" },
  });
  pipe(proc, "web");
  return proc;
}

export default async function globalSetup(): Promise<void> {
  const { proc: host, conn } = await startHost();
  const api = startApi(conn);
  await waitForHttp(`${API_URL}/health`, "API");
  log("API healthy.");

  const web = startWeb();
  await waitForHttp(WEB_URL, "web");
  log("web ready — stack is up.");

  // Handles for teardown (separate module execution, so persist to disk).
  fs.writeFileSync(STATE, JSON.stringify({ host: host.pid, api: api.pid, web: web.pid }));
}
