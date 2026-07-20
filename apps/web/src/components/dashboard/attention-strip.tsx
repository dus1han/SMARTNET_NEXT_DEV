"use client";

import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { ShieldAlert } from "lucide-react";
import { me } from "@/lib/auth";
import { getDataExceptions, type CompanyFilter } from "@/lib/reports";

/**
 * NEEDS ATTENTION.
 *
 * The data-exceptions screen has had live detection for every known legacy defect since Phase 4, and
 * nothing on the landing page ever said so — you learned there were 155 open exceptions by navigating
 * to a report and looking. A defect nobody is shown is a defect nobody works down, and two of these
 * block cutover outright (orphaned document lines against any foreign key, and the duplicate quotation
 * number against the `quotation_h` unique index).
 *
 * <h3>Why this is calm rather than alarming</h3>
 *
 * The backlog is real and will not clear this week, so a red banner would be permanently red — and a
 * warning that is always on is a warning nobody reads within about a fortnight. This states the number
 * in the same voice as the rest of the dashboard and links through. It disappears entirely at zero,
 * which is the only state worth celebrating.
 *
 * <h3>Why it fetches separately</h3>
 *
 * Its own query, not a field on the dashboard payload. The dashboard's analytics endpoint is already
 * the slowest thing on the page and has been seen to time out against the remote database; folding
 * another aggregate into it would mean one slow scan takes the whole screen down. Failure here renders
 * nothing at all — an attention strip that cannot load its own number has nothing to say, and a broken
 * panel on the landing page is worse than an absent one.
 */
export function AttentionStrip({ company }: { company: CompanyFilter }) {
  // Cached by the shell, so this costs nothing. The exceptions endpoint is gated on general_ledger,
  // the same permission the report itself sits behind — asking without it would be a guaranteed 403.
  const session = useQuery({ queryKey: ["me"], queryFn: me });
  const permitted = session.data?.permissions.includes("general_ledger") ?? false;

  const exceptions = useQuery({
    queryKey: ["data-exceptions-count", company],
    queryFn: () => getDataExceptions(company),
    enabled: permitted,
    // A backlog does not move minute to minute, and this is the landing page — re-running eight
    // detection queries on every visit buys nothing.
    staleTime: 5 * 60 * 1000,
  });

  const total = exceptions.data?.total ?? 0;

  // Nothing to say: not permitted, still loading, failed, or — the good case — no exceptions.
  if (!permitted || exceptions.isPending || exceptions.error || total === 0) {
    return null;
  }

  const blocking = (exceptions.data?.orphanedLines ?? 0) + (exceptions.data?.duplicateNumbers ?? 0);

  return (
    <Link
      href="/reports/data-exceptions"
      className="flex items-center gap-3 rounded-xl border border-subtle bg-surface px-5 py-3 transition-colors hover:bg-surface-sunken"
    >
      <ShieldAlert className="size-5 shrink-0 text-warning-text" aria-hidden />

      <div className="min-w-0 flex-1">
        <p className="text-sm font-medium text-text">
          {total} data exception{total === 1 ? "" : "s"} in the imported legacy data
        </p>
        <p className="truncate text-xs text-muted">
          {blocking > 0
            ? `${blocking} of them will block the migration to live — review before cutover.`
            : "Known defects, listed live so they do not quietly grow."}
        </p>
      </div>

      <span className="shrink-0 text-sm text-muted">Review →</span>
    </Link>
  );
}
