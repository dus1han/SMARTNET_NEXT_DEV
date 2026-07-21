import { instantFromApi } from "@/lib/time";
import type { DocumentVersionDetail } from "@/lib/history";
import { fieldLabel, formatValue, snapshotFields } from "./diff";

/**
 * Prints one version of a document — the question `document_versions` exists to answer.
 *
 * "What changed?" is the audit log. "What did this invoice *look like* on 3 March — print it" is
 * this, and it is the thing the legacy system gets wrong: it reprints yesterday's document with
 * today's tax rates and today's company header, because it stores no snapshot at all. The snapshot
 * is self-contained, so what comes out of the printer here is the document as it was signed.
 *
 * It prints from the snapshot alone. It does not re-fetch the document, and it must never fall back
 * to doing so — a "historical" reprint assembled from current data is precisely the defect.
 */
export function printVersion(version: DocumentVersionDetail, title: string) {
  const window_ = window.open("", "_blank", "width=820,height=1000");

  if (!window_) {
    // Pop-ups blocked. The caller reports it; silently doing nothing is how a button earns a
    // reputation for being broken.
    throw new Error("The browser blocked the print window. Allow pop-ups for this site.");
  }

  const rows = snapshotFields(version.snapshot)
    .map(
      (row) =>
        `<tr><th>${escape(fieldLabel(row.field))}</th><td>${escape(formatValue(row.value))}</td></tr>`,
    )
    .join("");

  const changedBy = version.changedByName ?? "an account that no longer exists";
  // Was new Date(...).toUTCString() with no Z, so a UTC value was read as local and then converted back
  // to UTC — shifted twice. The printed history said the wrong time.
  const when = instantFromApi(version.changedAt)?.toUTCString() ?? "an unknown time";

  window_.document.write(`<!doctype html>
<html lang="en">
  <head>
    <meta charset="utf-8" />
    <title>${escape(title)} — version ${version.versionNo}</title>
    <style>
      body { font: 14px/1.5 ui-sans-serif, system-ui, sans-serif; margin: 32px; color: #111; }
      h1 { font-size: 18px; margin: 0; }
      p.meta { color: #555; margin: 4px 0 24px; }
      table { border-collapse: collapse; width: 100%; }
      th, td { border-bottom: 1px solid #ddd; padding: 6px 8px; text-align: left; vertical-align: top; }
      th { width: 34%; font-weight: 600; color: #333; }
      /* The banner is the point: a reprint that does not say it is a reprint gets filed as the
         current document, and then argued about. */
      .stamp { border: 1px solid #999; padding: 8px 12px; margin-bottom: 20px; font-size: 12px; }
    </style>
  </head>
  <body>
    <div class="stamp">
      Historical version ${version.versionNo} — reproduced exactly as saved on ${escape(when)}.
      This is not necessarily the current document.
    </div>

    <h1>${escape(title)}</h1>
    <p class="meta">Saved by ${escape(changedBy)}${version.reason ? ` — ${escape(version.reason)}` : ""}</p>

    <table>${rows}</table>
  </body>
</html>`);

  window_.document.close();
  window_.focus();
  window_.print();
}

/** The snapshot is data, not markup. Anything from it that reaches HTML goes through here. */
function escape(value: string): string {
  return value
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;")
    .replace(/'/g, "&#39;");
}
