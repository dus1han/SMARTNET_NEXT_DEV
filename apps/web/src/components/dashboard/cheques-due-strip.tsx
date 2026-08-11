"use client";

import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { ScrollText } from "lucide-react";
import { me } from "@/lib/auth";
import { getChequesDueSoon } from "@/lib/cheques";
import type { CompanyFilter } from "@/lib/reports";
import { formatMoney } from "@/components/reports";

/**
 * CHEQUES DUE SOON.
 *
 * The cheques that become bankable within the next two <b>business</b> days, for anyone who may see the
 * cheque register. Two calendar days from a Friday is Sunday, so the cheque presented on Monday morning
 * — the one there is least time to fund — would be the one never warned about; the window is counted in
 * working days on the server, which also decides what "today" is.
 *
 * <h3>Why here rather than a pop-up after login</h3>
 *
 * Signing in lands on this page, so this is the screen that follows a login. A dialog would be an extra
 * click in front of a panel the user is about to see anyway, and one that appears every morning is one
 * that gets dismissed without reading inside a fortnight. This is only present when there is something
 * to say, which is what keeps it worth reading.
 *
 * <h3>Why it disappears</h3>
 *
 * At zero it renders nothing — the same rule as the attention strip beside it. It also renders nothing
 * for a user without the cheque permission, and nothing if its own query fails: a warning that cannot
 * load its own numbers has nothing to say, and a broken panel on the landing page is worse than an
 * absent one.
 */
export function ChequesDueStrip({ company }: { company: CompanyFilter }) {
  // Cached by the shell, so this costs nothing. The endpoint is gated on `cheques`, the same permission
  // the register and the print button sit behind — asking without it would be a guaranteed 403.
  const session = useQuery({ queryKey: ["me"], queryFn: me });
  const permitted = session.data?.permissions.includes("cheques") ?? false;

  const due = useQuery({
    queryKey: ["cheques-due-soon", company],
    queryFn: () => getChequesDueSoon(company),
    enabled: permitted,
    // The window moves by the day, not by the minute.
    staleTime: 5 * 60 * 1000,
  });

  const cheques = due.data?.cheques ?? [];

  // Nothing to say: not permitted, still loading, failed, or — the ordinary case — nothing due.
  if (!permitted || due.isPending || due.error || cheques.length === 0) {
    return null;
  }

  const total = cheques.reduce((sum, cheque) => sum + cheque.amount, 0);

  // Named rather than counted where it is short: "Pilot Stationers and 2 others" is read at a glance,
  // where "3 cheques" sends you to the register to find out whose.
  const payees =
    cheques.length === 1
      ? cheques[0].payTo
      : `${cheques[0].payTo} and ${cheques.length - 1} other${cheques.length === 2 ? "" : "s"}`;

  return (
    <Link
      href="/cheques"
      className="flex items-center gap-3 rounded-xl border border-warning bg-warning-subtle px-5 py-3 transition-colors hover:bg-surface-sunken"
    >
      <ScrollText className="size-5 shrink-0 text-warning-text" aria-hidden />

      <div className="min-w-0 flex-1">
        <p className="text-sm font-medium text-text">
          {cheques.length} cheque{cheques.length === 1 ? "" : "s"} due within 2 working days —{" "}
          {formatMoney(total)}
        </p>
        <p className="truncate text-xs text-muted">{payees}</p>
      </div>

      <span className="shrink-0 text-sm text-muted">Open →</span>
    </Link>
  );
}
