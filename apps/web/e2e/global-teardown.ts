import fs from "node:fs";
import path from "node:path";
import { execSync } from "node:child_process";

const STATE = path.join(__dirname, ".e2e-state.json");

function killTree(pid?: number): void {
  if (!pid) return;
  try {
    if (process.platform === "win32") {
      execSync(`taskkill /PID ${pid} /T /F`, { stdio: "ignore" });
    } else {
      process.kill(pid, "SIGKILL");
    }
  } catch {
    /* already gone */
  }
}

export default async function globalTeardown(): Promise<void> {
  try {
    const s = JSON.parse(fs.readFileSync(STATE, "utf8")) as { host?: number; api?: number; web?: number };
    // web first, then API, then the host (which lets the throwaway container go — the reaper is backup).
    killTree(s.web);
    killTree(s.api);
    killTree(s.host);
    fs.rmSync(STATE, { force: true });
  } catch {
    /* nothing to tear down */
  }
}
